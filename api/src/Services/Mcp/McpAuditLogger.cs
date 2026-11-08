using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>Outcome recorded for an MCP request (FR-009).</summary>
public static class McpOutcome
{
    public const string Ok = "ok";
    public const string ToolError = "tool_error";
    public const string RpcError = "rpc_error";
    public const string Unauthorized = "unauthorized";
    public const string Disabled = "disabled";
}

/// <summary>
/// Writes one structured audit record per MCP request, including the validated actor,
/// operation, outcome and duration.
///
/// <para>
/// This logs rather than persisting rows. Structured entries flow through the OpenTelemetry
/// pipeline into Application Insights, avoiding a database write for every read while retaining
/// the same audit fields.
/// </para>
/// <para>
/// The acting user is taken from the request principal, never from anything the client asserts:
/// <c>clientInfo</c> is recorded for context but carries no authority (FR-004).
/// </para>
/// </summary>
public interface IMcpAuditLogger
{
    void Record(McpAuditRecord record);
}

/// <param name="Method">The JSON-RPC method, e.g. <c>tools/call</c>.</param>
/// <param name="Tool">The tool name for <c>tools/call</c>; null otherwise.</param>
/// <param name="ActingUser">Display name of the person the agent acted for.</param>
/// <param name="ActingUserObjectId">That person's Entra object id, or null when unauthenticated.</param>
/// <param name="ClientName">Self-reported MCP client name, for context only.</param>
/// <param name="Outcome">One of <see cref="McpOutcome"/>.</param>
/// <param name="ElapsedMs">Server-side duration.</param>
public sealed record McpAuditRecord(
    string Method,
    string? Tool,
    string ActingUser,
    string? ActingUserObjectId,
    string? ClientName,
    string Outcome,
    long ElapsedMs);

public class McpAuditLogger(ILogger<McpAuditLogger> logger) : IMcpAuditLogger
{
    public void Record(McpAuditRecord record) =>
        logger.LogInformation(
            "MCP {McpMethod} {McpTool} by {ActingUser} ({ActingUserObjectId}) from {ClientName} -> {Outcome} in {ElapsedMs}ms",
            record.Method,
            record.Tool ?? "-",
            record.ActingUser,
            record.ActingUserObjectId ?? "-",
            record.ClientName ?? "-",
            record.Outcome,
            record.ElapsedMs);

    /// <summary>
    /// Describes only the validated bearer principal. The directory/object pair is the audit
    /// identity; rejected requests never receive an anonymous contributor identity.
    /// </summary>
    public static (string DisplayName, string? ObjectId) DescribeCaller(HttpRequest request)
    {
        var key = GalleryIdentity.Key(request);
        return key is null ? ("Unauthenticated", null) :
            (GalleryIdentity.DisplayName(request), $"{key.Value.TenantId}/{key.Value.ObjectId}");
    }
}
