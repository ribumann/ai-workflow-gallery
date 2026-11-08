using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Mcp;
using Xunit;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Records every audit line so tests can assert FR-009's "who did what" trail.</summary>
internal sealed class RecordingAuditLogger : IMcpAuditLogger
{
    internal List<McpAuditRecord> Records { get; } = [];

    public void Record(McpAuditRecord record) => Records.Add(record);
}

/// <summary>Reads an MCP endpoint response body back into a <see cref="JsonObject"/>.</summary>
internal static class McpTestJson
{
    internal static JsonObject? ReadBody(IActionResult result) => result switch
    {
        ContentResult { Content: not null } content => (JsonObject)JsonNode.Parse(content.Content)!,
        ObjectResult { Value: JsonObject json } => json,
        _ => null,
    };

    /// <summary>The single text block of a tool result, parsed back into JSON.</summary>
    internal static JsonNode Payload(JsonObject toolResult) =>
        JsonNode.Parse(((JsonArray)toolResult["content"]!)[0]!["text"]!.GetValue<string>())!;

    internal static string Text(JsonObject toolResult) =>
        ((JsonArray)toolResult["content"]!)[0]!["text"]!.GetValue<string>();
}

/// <summary>
/// Builds a wired-up <see cref="Mcp"/> endpoint over a real (SQLite) database, plus the helpers
/// tests use to speak JSON-RPC to it.
/// </summary>
internal sealed class McpTestHost
{
    internal const string BaseUrl = "https://gallery.example.com";

    internal required Mcp Endpoint { get; init; }
    internal required RecordingAuditLogger Audit { get; init; }
    internal required McpToolCatalog Catalog { get; init; }
    internal required IGalleryMcpTools Tools { get; init; }

    /// <summary>
    /// Builds the endpoint. <paramref name="resolveUserFromRequest"/> swaps the fake current-user
    /// service for the real one, so identity is resolved from the validated bearer principal
    /// propagated through the MCP adapter to the delegated endpoint.
    /// </summary>
    internal static McpTestHost Create(
        AppDbContext db,
        User? user = null,
        bool enabled = true,
        bool writesEnabled = false,
        string? baseUrl = BaseUrl,
        bool resolveUserFromRequest = false,
        PublicDemoPolicy? publicDemoPolicy = null)
    {
        var isPublic = publicDemoPolicy?.Enabled == true;
        var options = new McpOptions
        {
            Enabled = enabled,
            PublicEnabled = isPublic,
            PublicDemoEnabled = isPublic,
            WritesEnabled = writesEnabled,
        };
        var catalog = new McpToolCatalog(options);
        ICurrentUserService currentUser = resolveUserFromRequest || isPublic
            ? new CurrentUserService(db)
            : new FakeCurrentUserService(user);

        var tools = new GalleryMcpTools(
            new Tasks(db, currentUser, new FakeBlobStorageService(), new FakeFileValidationService()),
            new Workflows(db, currentUser),
            new Methods(db, currentUser),
            new Reports(db, currentUser),
            new GalleryLinkBuilder(baseUrl));

        var audit = new RecordingAuditLogger();

        return new McpTestHost
        {
            Catalog = catalog,
            Tools = tools,
            Audit = audit,
            Endpoint = new Mcp(options, catalog, tools, audit, NullLogger<Mcp>.Instance, publicDemoPolicy),
        };
    }

    /// <summary>Sends one JSON-RPC message and returns the parsed response (null for a notification).</summary>
    internal Task<JsonObject?> SendAsync(string method, object? @params = null, object? id = null, User? signedInAs = null)
    {
        var envelope = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method, ["id"] = id ?? 1 };
        if (@params is not null) envelope["params"] = @params;

        return SendRawAsync(JsonSerializer.Serialize(envelope), signedInAs);
    }

    internal async Task<JsonObject?> SendRawAsync(string body, User? signedInAs = null) =>
        McpTestJson.ReadBody(await Endpoint.Post(Request(body, signedInAs), CancellationToken.None));

    internal Task<IActionResult> PostAsync(string body, User? signedInAs = null) =>
        Endpoint.Post(Request(body, signedInAs), CancellationToken.None);

    /// <summary>Calls a tool and returns the <c>result</c> member of the JSON-RPC response.</summary>
    internal async Task<JsonObject> CallToolAsync(string name, string collectionId, object? arguments = null, User? signedInAs = null)
    {
        var scoped = JsonSerializer.SerializeToNode(arguments ?? new { })!.AsObject();
        scoped["collectionId"] = collectionId;
        var response = await SendAsync("tools/call", new { name, arguments = scoped }, signedInAs: signedInAs);
        Assert.NotNull(response);
        Assert.Null(response!["error"]);
        return (JsonObject)response["result"]!;
    }

    internal static HttpRequest Request(string body, User? signedInAs = null, string? collectionId = null)
    {
        var request = TestHttpRequestFactory.CreateWithJsonBody(body);
        request.Method = HttpMethods.Post;
        request.Path = "/api/mcp";
        request.QueryString = collectionId is null
            ? QueryString.Empty
            : QueryString.Create("collectionId", collectionId);

        if (signedInAs is not null)
        {
            MethodTestSupport.Principal(request, signedInAs);
        }

        return request;
    }
}
