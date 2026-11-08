using Microsoft.Extensions.Configuration;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// Feature switches and protocol constants for the MCP server (feature 007). Read once at startup
/// from app settings, the same externalized-configuration pattern <c>EnableAiComposer</c> uses
/// (see Program.cs and infra/modules/function-app.bicep).
/// </summary>
public class McpOptions
{
    /// <summary>
    /// The newest revision this server implements, and what it answers with when a client asks
    /// for something it does not recognise.
    /// </summary>
    public const string DefaultProtocolVersion = "2026-07-28";

    /// <summary>
    /// Revisions a client may negotiate, newest first. All three share the same JSON-RPC surface
    /// this server implements, so honouring an older one costs nothing and avoids forcing clients
    /// that pin a revision to fail the handshake.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = new[]
    {
        DefaultProtocolVersion,
        "2025-11-25",
        "2025-06-18",
        "2025-03-26",
    };

    public const string ServerName = "ai-workflow-gallery";

    /// <summary>
    /// Master switch (<c>EnableMcpServer</c>). Off by default so the endpoint is inert everywhere
    /// until an environment deliberately opts in.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Separate public opt-in. The request boundary must also be in public-demo mode.</summary>
    public bool PublicEnabled { get; init; }
    public bool PublicDemoEnabled { get; init; }
    public IReadOnlyList<string> PublicAllowedOrigins { get; init; } = [];
    public bool PublicAvailable => Enabled && PublicEnabled;

    /// <summary>
    /// Private-mode write switch (<c>EnableMcpWrites</c>, FR-007): write capabilities must be
    /// disableable without affecting reads, and a disabled capability must not be advertised.
    /// Public Playground writes use the separate collection-scoped capability and database policy;
    /// this switch alone never enables them.
    /// </summary>
    private readonly bool _writesEnabled;
    public bool WritesEnabled { get => _writesEnabled && !PublicDemoEnabled; init => _writesEnabled = value; }

    public static McpOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Enabled = ReadSwitch(configuration, "EnableMcpServer"),
        PublicEnabled = ReadSwitch(configuration, "EnablePublicMcp"),
        PublicDemoEnabled = ReadSwitch(configuration, "PublicDemoEnabled"),
        PublicAllowedOrigins = ReadOrigins(configuration),
        WritesEnabled = ReadSwitch(configuration, "EnableMcpWrites"),
    };

    private static IReadOnlyList<string> ReadOrigins(IConfiguration configuration)
    {
        var value = configuration["PublicMcpAllowedOrigins"];
        if (value is null) return [];
        var origins = value.Split(',').Select(x => x.Trim()).ToArray();
        if (origins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            !string.Equals(uri.GetLeftPart(UriPartial.Authority), x, StringComparison.Ordinal)))
            throw new InvalidOperationException("PublicMcpAllowedOrigins must contain exact HTTPS origins separated by commas.");
        return origins.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool IsTransportRequest(Microsoft.AspNetCore.Http.HttpRequest request) =>
        request.Path.Equals("/api/mcp", StringComparison.OrdinalIgnoreCase) &&
        (Microsoft.AspNetCore.Http.HttpMethods.IsPost(request.Method) ||
         Microsoft.AspNetCore.Http.HttpMethods.IsGet(request.Method) ||
         Microsoft.AspNetCore.Http.HttpMethods.IsDelete(request.Method));

    private static bool ReadSwitch(IConfiguration configuration, string name)
    {
        var value = configuration[name];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled))
            throw new InvalidOperationException($"{name} must be true or false.");
        return enabled;
    }

    /// <summary>
    /// Resolves the revision to answer <c>initialize</c> with: the client's request when it is
    /// supported, otherwise this server's default. Never fails the handshake over a version.
    /// </summary>
    public static string NegotiateProtocolVersion(string? requested) =>
        !string.IsNullOrWhiteSpace(requested) &&
        SupportedProtocolVersions.Contains(requested, StringComparer.Ordinal)
            ? requested
            : DefaultProtocolVersion;
}
