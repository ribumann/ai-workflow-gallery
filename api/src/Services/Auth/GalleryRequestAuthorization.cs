using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Services.Auth;

/// <summary>One HTTP boundary for browser and MCP calls, also hosted in HTTP pipeline tests.</summary>
public sealed class GalleryRequestAuthorization(
    IConfiguration configuration,
    IEntraTokenValidator validator,
    ILogger<GalleryRequestAuthorization> logger,
    PublicDemoPolicy? publicDemoPolicy = null,
    CollectionAuthorizationPolicy? collectionAuthorizationPolicy = null)
{
    private readonly PublicDemoPolicy _policy = publicDemoPolicy ?? PublicDemoPolicy.FromConfiguration(configuration);
    private readonly CollectionAuthorizationPolicy _collections = collectionAuthorizationPolicy ?? CollectionAuthorizationPolicy.Read(configuration);
    public bool PublicDemoEnabled => _policy.Enabled;
    public bool IsPlaygroundWrite(HttpRequest request) => _policy.AllowsWrite(request);

    public async Task<bool> AuthorizeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Response.Headers.CacheControl = "no-store";
        _policy.Stamp(context);
        _collections.Stamp(context);
        if (_policy.Enabled)
        {
            if (McpOptions.IsTransportRequest(context.Request))
            {
                try
                {
                    if (McpOptions.FromConfiguration(configuration).PublicAvailable &&
                        CollectionRequestContext.TryResolve(context.Request, _policy, out _))
                        return true;
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogError(ex, "Public MCP configuration is invalid.");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { error = "mcp_unavailable" }, cancellationToken);
                    return false;
                }
            }
            if (_policy.AllowsRead(context.Request)) return true;
            if (_policy.AllowsWrite(context.Request)) return true;
            // This exception is proposal-only, not an anonymous write identity. The handler
            // checks complete configuration, scoped input, and SQL spend before inference.
            if (PublicComposerSettings.IsProposalRequest(context.Request) &&
                PublicComposerSettings.Read(configuration).OptedIn &&
                CollectionScope.IsValid(context.Request.Query["collectionId"].ToString())) return true;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "public_demo_read_only" }, cancellationToken);
            return false;
        }
        GalleryAuthSettings settings;
        McpOptions mcp;
        try
        {
            settings = GalleryAuthSettings.Read(configuration);
            mcp = McpOptions.FromConfiguration(configuration);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Gallery authentication configuration is invalid.");
            await Reject(StatusCodes.Status503ServiceUnavailable, "authentication_unavailable");
            return false;
        }

        if (!mcp.Enabled && context.Request.Path.Equals("/api/mcp", StringComparison.OrdinalIgnoreCase))
        {
            await Reject(StatusCodes.Status503ServiceUnavailable, "mcp_disabled");
            return false;
        }

        var header = context.Request.Headers.Authorization;
        var token = header.Count == 1 && AuthenticationHeaderValue.TryParse(header[0], out var authorization) &&
            string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? authorization.Parameter : null;

        GalleryAuthResult result;
        try
        {
            result = await validator.ValidateAsync(token, settings, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException ||
                                   ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Entra validation metadata could not be obtained.");
            await Reject(StatusCodes.Status503ServiceUnavailable, "authentication_unavailable");
            return false;
        }

        if (result.Status == GalleryAuthStatus.Success && result.Principal is not null)
        {
            context.User = result.Principal;
            if (!_collections.HasAccess(context.Request))
            {
                logger.LogWarning("Gallery request rejected by collection membership policy.");
                await Reject(StatusCodes.Status403Forbidden, "collection_access_denied");
                return false;
            }
            return true;
        }

        if (result.Status == GalleryAuthStatus.Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate =
                $"Bearer {(mcp.Enabled ? $"resource_metadata=\"{settings.MetadataUrl}\", " : "")}scope=\"api://{settings.Audience}/{settings.Scope}\"";
            await Reject(StatusCodes.Status401Unauthorized, "authentication_required");
        }
        else
        {
            logger.LogWarning("Gallery request rejected by tenant or delegated-scope policy.");
            await Reject(StatusCodes.Status403Forbidden, "access_denied");
        }
        return false;

        async Task Reject(int status, string error)
        {
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error }, cancellationToken);
        }
    }
}
