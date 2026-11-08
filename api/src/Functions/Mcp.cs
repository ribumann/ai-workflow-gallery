using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

/// <summary>
/// `/api/mcp` — the gallery's Model Context Protocol server (feature 007).
///
/// <para>
/// <b>Why this is an ordinary HTTP function.</b> Sitting on the same route surface as every other
/// endpoint means it inherits the gallery's deployment boundary. Private deployments use bearer auth:
/// <see cref="Middleware.TenantAuthorizationMiddleware"/> validates delegated identity and admission,
/// then the trusted principal flows through every delegated read. FR-003 forbids a privileged path
/// around those rules. Explicit public deployments instead retain an anonymous public-only stamp
/// through those same handlers and expose only the bounded read allowlist.
/// </para>
/// <para>
/// <b>Transport.</b> MCP Streamable HTTP in JSON-response mode: one JSON-RPC message per POST, one
/// JSON response back. No SSE stream is opened and no session id is issued, because every read tool
/// answers in a single round trip and the Function App scales out — per-instance session state
/// would be wrong once the app scales out.
/// </para>
/// </summary>
public class Mcp(
    McpOptions options,
    McpToolCatalog catalog,
    IGalleryMcpTools tools,
    IMcpAuditLogger auditLogger,
    ILogger<Mcp> logger,
    PublicDemoPolicy? publicDemoPolicy = null,
    PublicMcpGuard? publicGuard = null)
{
    private const string InitializeMethod = "initialize";
    private const string InitializedNotification = "notifications/initialized";
    private const string PingMethod = "ping";
    private const string ToolsListMethod = "tools/list";
    private const string ToolsCallMethod = "tools/call";

    [Function("McpEndpoint")]
    public async Task<IActionResult> Post(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "mcp")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var policy = publicDemoPolicy ?? PublicDemoPolicy.For(req);
        if (!policy.Enabled) return await ProcessAsync(req, cancellationToken);
        policy.Stamp(req.HttpContext);
        if (!options.PublicAvailable ||
            !CollectionRequestContext.TryResolve(req, policy, out _))
            return PublicDemoPolicy.Denied();
        if (!OriginAllowed(req)) return TransportError(403, "mcp_origin_denied");
        var lease = (publicGuard ?? PublicMcpGuard.Worker).TryEnter(out var retryAfter);
        if (lease is null)
        {
            req.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TransportError(429, "mcp_busy");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, req.HttpContext.RequestAborted);
        deadline.CancelAfter(PublicMcpGuard.Deadline);
        var boundedToken = deadline.Token;
        try
        {
            // The lease belongs to the underlying operation, not the HTTP waiter. Even an
            // uncooperative dependency must not free a concurrency slot while still running.
            return await ExecuteLeased().WaitAsync(boundedToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !req.HttpContext.RequestAborted.IsCancellationRequested)
        {
            return TransportError(504, "mcp_deadline_exceeded");
        }

        async Task<IActionResult> ExecuteLeased()
        {
            using (lease)
            {
                var result = await ProcessAsync(req, boundedToken);
                boundedToken.ThrowIfCancellationRequested();
                if (result is ContentResult { Content: not null } content &&
                    Encoding.UTF8.GetByteCount(content.Content) > PublicMcpGuard.MaxResponseBytes)
                    return TransportError(413, "mcp_response_too_large");
                return result;
            }
        }
    }

    private async Task<IActionResult> ProcessAsync(HttpRequest req, CancellationToken cancellationToken)
    {
        var isPublic = (publicDemoPolicy ?? PublicDemoPolicy.For(req)).Enabled;
        var stopwatch = Stopwatch.StartNew();
        var (callerName, callerObjectId) = McpAuditLogger.DescribeCaller(req);

        if (!options.Enabled)
        {
            Audit("(disabled)", null, null, McpOutcome.Disabled);
            return DisabledResult();
        }

        string body;
        if (isPublic)
        {
            if (req.ContentLength > PublicMcpGuard.MaxRequestBytes)
                return TransportError(413, "mcp_request_too_large");
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await req.Body.ReadAsync(chunk.AsMemory(0,
                       Math.Min(chunk.Length, PublicMcpGuard.MaxRequestBytes + 1 - (int)buffer.Length)), cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > PublicMcpGuard.MaxRequestBytes)
                    return TransportError(413, "mcp_request_too_large");
            }
            body = Encoding.UTF8.GetString(buffer.ToArray());
        }
        else
        {
            using var reader = new StreamReader(req.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        if (!McpJsonRpc.TryParse(body, out var request, out var failure))
        {
            Audit("(unparseable)", null, null, McpOutcome.RpcError);
            return Json(McpJsonRpc.Error(null, failure!.Code, failure.Message));
        }

        var clientName = isPublic ? null : ReadClientName(request);

        // A notification carries no id and must not be answered with a JSON-RPC body. It is still
        // audited, so the trail shows the full conversation rather than only the parts that
        // happened to expect a reply.
        if (request.IsNotification)
        {
            Audit(request.Method, null, clientName, McpOutcome.Ok);
            return new AcceptedResult();
        }

        try
        {
            switch (request.Method)
            {
                case InitializeMethod:
                    Audit(request.Method, null, clientName, McpOutcome.Ok);
                    return Json(McpJsonRpc.Success(request.Id, BuildInitializeResult(req, request)));

                case PingMethod:
                    Audit(request.Method, null, clientName, McpOutcome.Ok);
                    return Json(McpJsonRpc.Success(request.Id, new JsonObject()));

                case ToolsListMethod:
                    Audit(request.Method, null, clientName, McpOutcome.Ok);
                    return Json(McpJsonRpc.Success(request.Id, BuildToolsList(req, isPublic)));

                case ToolsCallMethod:
                    return await CallToolAsync(req, request, clientName, Audit, cancellationToken);

                default:
                    Audit(request.Method, null, clientName, McpOutcome.RpcError);
                    return Json(McpJsonRpc.Error(
                        request.Id,
                        McpErrorCodes.MethodNotFound,
                        $"Unknown method '{request.Method}'."));
            }
        }
        catch (McpArgumentException ex)
        {
            Audit(request.Method, ReadToolName(request), clientName, McpOutcome.RpcError);
            return Json(McpJsonRpc.Error(request.Id, McpErrorCodes.InvalidParams, ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything that escapes a tool must still come back as JSON-RPC: an MCP client that
            // receives the host's raw 500 reports a transport failure and usually drops the
            // connection, when what actually happened is one failed call. Auditing here also
            // closes the FR-009 hole where exactly the failures worth investigating were the ones
            // that left no record. The message is deliberately generic — the detail goes to the
            // log, not to the caller.
            if (isPublic) logger.LogError("Public MCP request failed.");
            else logger.LogError(ex, "Unhandled exception while handling MCP method {Method}.", request.Method);
            Audit(request.Method, ReadToolName(request), clientName, McpOutcome.RpcError);
            return Json(McpJsonRpc.Error(
                request.Id,
                McpErrorCodes.InternalError,
                "The gallery could not complete this request. The failure has been logged."));
        }

        void Audit(string method, string? tool, string? client, string outcome) =>
            auditLogger.Record(new McpAuditRecord(
                isPublic && method is not (InitializeMethod or InitializedNotification or PingMethod or ToolsListMethod or ToolsCallMethod)
                    ? "(unknown)" : method,
                isPublic && (tool is null || catalog.Find(tool, CollectionRequestContext.For(req)) is null) ? null : tool,
                callerName, callerObjectId, client, outcome, stopwatch.ElapsedMilliseconds));
    }

    /// <summary>
    /// Clients that terminate a session issue a DELETE. There is no state to drop, so this
    /// succeeds rather than 405-ing a client that is doing the right thing.
    /// </summary>
    [Function("McpEndpointDelete")]
    public IActionResult Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "mcp")] HttpRequest req) =>
        (publicDemoPolicy ?? PublicDemoPolicy.For(req)).Enabled
            ? !options.PublicAvailable ||
              !CollectionRequestContext.TryResolve(req, publicDemoPolicy ?? PublicDemoPolicy.For(req), out _) ? PublicDemoPolicy.Denied() :
                !OriginAllowed(req) ? TransportError(403, "mcp_origin_denied") : new NoContentResult() :
            options.Enabled ? new NoContentResult() : DisabledResult();

    /// <summary>
    /// A GET on the MCP endpoint asks the server to open a server-to-client event stream. This
    /// server has nothing to push — no subscriptions, no long-running tools, no server-initiated
    /// requests — so it declines explicitly instead of holding a connection open for nothing.
    /// </summary>
    [Function("McpEndpointGet")]
    public IActionResult Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "mcp")] HttpRequest req)
    {
        if ((publicDemoPolicy ?? PublicDemoPolicy.For(req)).Enabled)
        {
            if (!options.PublicAvailable ||
                !CollectionRequestContext.TryResolve(req, publicDemoPolicy ?? PublicDemoPolicy.For(req), out _))
                return PublicDemoPolicy.Denied();
            if (!OriginAllowed(req)) return TransportError(403, "mcp_origin_denied");
        }
        if (!options.Enabled)
        {
            return DisabledResult();
        }

        req.HttpContext.Response.Headers.Append("Allow", "POST, DELETE");
        return new ObjectResult(McpJsonRpc.Error(
            null,
            McpErrorCodes.InvalidRequest,
            "This server does not offer a server-to-client stream. Send JSON-RPC messages with POST."))
        {
            StatusCode = StatusCodes.Status405MethodNotAllowed,
            ContentTypes = { "application/json" },
        };
    }

    private async Task<IActionResult> CallToolAsync(
        HttpRequest req,
        McpRequest request,
        string? clientName,
        Action<string, string?, string?, string> audit,
        CancellationToken cancellationToken)
    {
        var toolName = ReadToolName(request)
            ?? throw new McpArgumentException("\"params.name\" is required and must be a tool name.");

        // Resolving through the catalog is what keeps a policy-hidden capability indistinguishable
        // from one that never existed (FR-007).
        var isPublic = (publicDemoPolicy ?? PublicDemoPolicy.For(req)).Enabled;
        if (catalog.Find(toolName, isPublic ? CollectionRequestContext.For(req) : null) is null)
        {
            audit(ToolsCallMethod, toolName, clientName, McpOutcome.RpcError);
            return Json(McpJsonRpc.Error(request.Id, McpErrorCodes.InvalidParams, $"Unknown tool '{toolName}'."));
        }

        var arguments = request.Params?["arguments"] as JsonObject;
        var result = await tools.ExecuteAsync(toolName, arguments, req, cancellationToken);

        audit(ToolsCallMethod, toolName, clientName, result.IsError ? McpOutcome.ToolError : McpOutcome.Ok);
        return Json(McpJsonRpc.Success(request.Id, BuildToolResult(result)));
    }

    private static JsonObject BuildToolResult(McpToolResult result)
    {
        if (result.IsError)
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(TextContent(result.Message ?? "The tool could not complete.")),
                ["isError"] = true,
            };
        }

        var payload = result.Payload!;

        return new JsonObject
        {
            // Text content is what every MCP client can render today; structuredContent is what a
            // client that understands it should parse. Both carry the same payload, so no client
            // sees a reduced view.
            ["content"] = new JsonArray(TextContent(payload.ToJsonString(McpJsonRpc.ResponseOptions))),
            ["structuredContent"] = payload.DeepClone(),
            ["isError"] = false,
        };
    }

    private static JsonObject TextContent(string text) => new()
    {
        ["type"] = "text",
        ["text"] = text,
    };

    private JsonObject BuildInitializeResult(HttpRequest req, McpRequest request)
    {
        var context = CollectionRequestContext.For(req);
        var publicWriteEnabled = context?.Capabilities.IsPublicDemo == true &&
            options.PublicAvailable && context.CollectionId == CollectionScope.Playground &&
            context.Capabilities.CanWrite;
        var writeEnabled = publicWriteEnabled || options.WritesEnabled;
        // Prefer what the client asked for in params; fall back to the transport header, which is
        // how a client that has already negotiated once identifies the revision it is speaking.
        var requested = AsString(request.Params?["protocolVersion"])
            ?? req.Headers["MCP-Protocol-Version"].FirstOrDefault();

        return new JsonObject
        {
            ["protocolVersion"] = McpOptions.NegotiateProtocolVersion(requested),
            ["capabilities"] = new JsonObject
            {
                // The catalog is compiled in, so it never changes within a server's lifetime and
                // clients need not subscribe to change notifications.
                ["tools"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = McpOptions.ServerName,
                ["title"] = "AI Workflow Gallery",
                ["version"] = typeof(Mcp).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            },
            ["instructions"] =
                "Search this gallery before improvising an approach: it holds how this organisation " +
                "already works with AI — reusable tasks, end-to-end workflows, AI methods, and a " +
                "directory of reports and AI apps. Cite the galleryUrl of anything you use so a " +
                "person can verify it. " +
                // Derived rather than fixed: these instructions are the highest-trust guidance a
                // client gets, so stating "read-only" on a server that can write would invite it to
                // skip the human confirmation the write tools ask for.
                (writeEnabled
                    ? publicWriteEnabled
                      ? "This connection is scoped to the shared, public and disposable Playground. " +
                        "It can add and change catalog content there; confirm the proposed change with " +
                        "the person before calling a create, update or delete tool. It cannot change " +
                        "the published collections."
                      : "This server can also add and change gallery content on the signed-in " +
                      "person's behalf: have them confirm what you are about to write before you " +
                      "call a create, update or delete tool."
                    : "This server is read-only."),
        };
    }

    private JsonObject BuildToolsList(HttpRequest req, bool isPublic) => new()
    {
        ["tools"] = new JsonArray(catalog.VisibleFor(isPublic ? CollectionRequestContext.For(req) : null).Select(tool =>
        {
            var json = tool.ToJson();
            var schema = json["inputSchema"]!.AsObject();
            var properties = schema["properties"]!.AsObject();
            var context = isPublic ? CollectionRequestContext.For(req) : null;
            properties["collectionId"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = context is null
                    ? new JsonArray(CollectionCapabilities.Collections.Select(x => (JsonNode?)JsonValue.Create(x.Id)).ToArray())
                    : new JsonArray(context.CollectionId),
                ["description"] = context is null
                    ? "Select the collection to use for this call."
                    : "Must match the collection selected for this MCP connection.",
            };
            var required = schema["required"] as JsonArray ?? new JsonArray();
            if (!schema.ContainsKey("required")) schema["required"] = required;
            if (!required.Any(value => value?.GetValue<string>() == "collectionId"))
                required.Add("collectionId");

            if (isPublic)
            {
                if (properties["pageSize"] is JsonObject page)
                {
                    page["maximum"] = PublicMcpGuard.MaxPageSize;
                    page["description"] = $"Results per page, at most {PublicMcpGuard.MaxPageSize}. Larger pages are rejected.";
                }

                if (tool.RequiresWrite)
                    json["description"] = tool.Description
                        .Replace("the signed-in person", "the shared Playground contributor", StringComparison.Ordinal)
                        .Replace("signed-in person", "shared Playground contributor", StringComparison.Ordinal);
            }
            return (JsonNode)json;
        }).ToArray()),
    };

    private bool OriginAllowed(HttpRequest req) =>
        !req.Headers.TryGetValue("Origin", out var origin) ||
        origin.Count == 1 && options.PublicAllowedOrigins.Contains(origin[0]!, StringComparer.Ordinal);

    private static IActionResult TransportError(int status, string error) =>
        new ObjectResult(new { error }) { StatusCode = status };

    private static string? ReadClientName(McpRequest request) =>
        AsString(request.Params?["clientInfo"]?["name"]);

    private static string? ReadToolName(McpRequest request)
    {
        var name = AsString(request.Params?["name"]);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Reads a node as a string only when it really is one. A client that sends a number or an
    /// object where a string belongs should get a protocol error, not an unhandled cast deep in
    /// the handler.
    /// </summary>
    private static string? AsString(JsonNode? node) =>
        node is not null && node.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : null;

    private static IActionResult Json(JsonObject payload) =>
        new ContentResult
        {
            Content = payload.ToJsonString(McpJsonRpc.ResponseOptions),
            ContentType = "application/json",
            StatusCode = StatusCodes.Status200OK,
        };

    /// <summary>
    /// Explicitly unavailable rather than advertising an unusable MCP resource.
    /// </summary>
    private static IActionResult DisabledResult() =>
        new ObjectResult(new
        {
            error = "mcp_disabled",
            message = "The MCP server is not enabled in this environment.",
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
}
