using Microsoft.AspNetCore.Http;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// Builds the "link back to its page" that every MCP result carries (User Story 1, AS3) so a human
/// can verify what an agent read.
///
/// <para>
/// Resolving the gallery's public origin is not as simple as reading the request's own host,
/// because the API runs as a Static Web Apps <em>linked backend</em>: the Function App sees its own
/// hostname, not the one the person browses. The origin is therefore resolved in decreasing order
/// of trustworthiness — an explicit setting, then what the proxy tells us, then the request itself.
/// </para>
/// <para>
/// These are links, not content. Nothing here fetches anything, and a report's own destination is
/// returned as stored data and never dereferenced (FR-010).
/// </para>
/// </summary>
public class GalleryLinkBuilder(string? configuredBaseUrl = null)
{
    private readonly string? _configuredBaseUrl = Normalize(configuredBaseUrl);

    public const string TaskPath = "tasks";
    public const string WorkflowPath = "workflows";
    public const string MethodPath = "methods";
    public const string ReportPath = "reports";

    /// <summary>Absolute URL of an item's page in the gallery, or null if no origin can be resolved.</summary>
    public string? BuildItemUrl(HttpRequest request, string path, Guid id)
    {
        var origin = ResolveOrigin(request);
        var collection = request.Query["collectionId"].ToString();
        var suffix = CollectionScope.IsValid(collection) ? $"?collectionId={collection}" : "";
        return origin is null ? null : $"{origin}/{path}/{id}{suffix}";
    }

    private string? ResolveOrigin(HttpRequest request)
    {
        if (_configuredBaseUrl is not null)
        {
            return _configuredBaseUrl;
        }

        // Static Web Apps forwards the browser-facing URL it was asked for; its origin is the one
        // the person actually uses.
        if (request.Headers.TryGetValue("x-ms-original-url", out var originalUrl) &&
            Uri.TryCreate(originalUrl.FirstOrDefault(), UriKind.Absolute, out var parsed))
        {
            return SafeOrigin(parsed.GetLeftPart(UriPartial.Authority));
        }

        var forwardedHost = request.Headers["x-forwarded-host"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedHost))
        {
            var scheme = request.Headers["x-forwarded-proto"].FirstOrDefault() ?? "https";
            var candidate = SafeOrigin($"{scheme}://{forwardedHost.Split(',')[0].Trim()}");
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return request.Host.HasValue ? SafeOrigin($"{request.Scheme}://{request.Host.Value}") : null;
    }

    /// <summary>
    /// Accepts an origin only if it is an absolute http(s) URL with a plain host and nothing else.
    ///
    /// <para>
    /// Two of the three fallbacks read caller-supplied headers, and the result is handed to an
    /// agent as the citable link for authentic gallery content — so an unvalidated origin would let
    /// a caller attach an arbitrary destination (or a <c>javascript:</c> scheme) to a real record.
    /// The blast radius is narrow, since the link only ever returns to the caller who supplied the
    /// header, but validating is cheap and the alternative is a link nobody should trust.
    /// </para>
    /// </summary>
    private static string? SafeOrigin(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return Normalize(uri.GetLeftPart(UriPartial.Authority));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
}
