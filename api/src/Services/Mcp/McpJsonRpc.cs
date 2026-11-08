using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// JSON-RPC 2.0 codes used by the MCP endpoint. The four negative codes are the standard ones;
/// MCP adds no codes of its own for the surface implemented here (tool failures are reported
/// inside a successful result via <c>isError</c>, not as protocol errors).
/// </summary>
public static class McpErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

/// <summary>
/// A single inbound JSON-RPC message. MCP revision 2025-06-18 and later forbid batching, so this
/// deliberately models one message rather than a list — see <see cref="McpJsonRpc.TryParse"/>.
/// </summary>
public sealed record McpRequest(string Method, JsonNode? Id, JsonObject? Params)
{
    /// <summary>
    /// A message without an <c>id</c> is a notification: the server acts on it but must not send
    /// a response body back.
    /// </summary>
    public bool IsNotification => Id is null;
}

/// <summary>Why an inbound message could not be turned into an <see cref="McpRequest"/>.</summary>
public sealed record McpParseFailure(int Code, string Message);

/// <summary>
/// Parsing and construction of the JSON-RPC envelope. Kept free of HTTP and of gallery domain
/// types so it can be unit-tested directly.
/// </summary>
public static class McpJsonRpc
{
    public const string Version = "2.0";

    /// <summary>
    /// Parses one inbound message. Returns false with a <paramref name="failure"/> carrying the
    /// JSON-RPC code the caller should report:
    /// <list type="bullet">
    /// <item><description><c>-32700</c> when the payload is not JSON at all.</description></item>
    /// <item><description><c>-32600</c> when it is JSON but not a conforming single request —
    /// including a batch array, which this protocol revision does not allow.</description></item>
    /// </list>
    /// </summary>
    public static bool TryParse(string body, out McpRequest request, out McpParseFailure? failure)
    {
        request = null!;
        failure = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "A JSON-RPC request body is required.");
            return false;
        }

        JsonNode? node;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { AllowTrailingCommas = true });
            if (HasDuplicateProperties(document.RootElement))
            {
                failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "Duplicate JSON properties are not allowed.");
                return false;
            }
            node = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            failure = new McpParseFailure(McpErrorCodes.ParseError, "The request body is not valid JSON.");
            return false;
        }

        static bool HasDuplicateProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    if (HasDuplicateProperties(item)) return true;
            return false;
        }

        if (node is JsonArray)
        {
            failure = new McpParseFailure(
                McpErrorCodes.InvalidRequest,
                "JSON-RPC batching is not supported. Send one message per request.");
            return false;
        }

        if (node is not JsonObject obj)
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "A JSON-RPC request must be a JSON object.");
            return false;
        }

        // The `jsonrpc` member is required to be exactly "2.0". Being strict here is cheap and
        // stops a malformed client from being silently half-understood.
        var versionNode = obj["jsonrpc"];
        if (versionNode is null || versionNode.GetValueKind() != JsonValueKind.String ||
            !string.Equals(versionNode.GetValue<string>(), Version, StringComparison.Ordinal))
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "\"jsonrpc\" must be \"2.0\".");
            return false;
        }

        var methodNode = obj["method"];
        if (methodNode is null || methodNode.GetValueKind() != JsonValueKind.String)
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "\"method\" is required and must be a string.");
            return false;
        }

        var method = methodNode.GetValue<string>();
        if (string.IsNullOrWhiteSpace(method))
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "\"method\" must not be empty.");
            return false;
        }

        var idNode = obj["id"];
        if (idNode is not null)
        {
            var kind = idNode.GetValueKind();
            if (kind != JsonValueKind.String && kind != JsonValueKind.Number)
            {
                failure = new McpParseFailure(McpErrorCodes.InvalidRequest, "\"id\" must be a string or a number.");
                return false;
            }
        }

        if (obj["params"] is { } paramsNode && paramsNode is not JsonObject)
        {
            failure = new McpParseFailure(McpErrorCodes.InvalidParams, "\"params\" must be an object.");
            return false;
        }

        // Detach the nodes from the parsed document so callers can reuse them in a response
        // without hitting JsonNode's single-parent restriction.
        request = new McpRequest(
            method,
            idNode is null ? null : JsonNode.Parse(idNode.ToJsonString()),
            obj["params"]?.DeepClone() as JsonObject);
        return true;
    }

    public static JsonObject Success(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = Version,
        ["id"] = id,
        ["result"] = result,
    };

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = Version,
        ["id"] = id,
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };

    /// <summary>
    /// Serializes a <see cref="JsonNode"/> tree without escaping characters that are legal in JSON
    /// string values, so gallery content (quotes, angle brackets, non-ASCII) reads back unchanged.
    /// </summary>
    public static readonly JsonSerializerOptions ResponseOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
