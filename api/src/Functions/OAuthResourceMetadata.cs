using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Services.Mcp;

namespace WorkflowGallery.Api.Functions;

public sealed class OAuthResourceMetadata(IConfiguration configuration, ILogger<OAuthResourceMetadata> logger)
{
    [Function("OAuthResourceMetadata")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth-resource-metadata")] HttpRequest req)
    {
        try
        {
            req.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!McpOptions.FromConfiguration(configuration).Enabled)
                return new ObjectResult(new { error = "mcp_disabled" }) { StatusCode = 503 };
            var settings = GalleryAuthSettings.Read(configuration);
            return new OkObjectResult(new
            {
                resource = settings.Resource.AbsoluteUri,
                authorization_servers = settings.Tenants.Where(settings.Admits).Order()
                    .Select(t => $"https://login.microsoftonline.com/{t}/v2.0"),
                scopes_supported = new[] { $"api://{settings.Audience}/{settings.Scope}" },
                bearer_methods_supported = new[] { "header" },
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Cannot publish OAuth resource metadata: authentication is not configured.");
            return new ObjectResult(new { error = "authentication_unavailable" }) { StatusCode = 503 };
        }
    }
}
