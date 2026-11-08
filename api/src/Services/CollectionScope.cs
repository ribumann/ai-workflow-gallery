using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Services;

public static class CollectionScope
{
    public const string PublicTechniques = "public-techniques";
    public const string CuratedTools = "curated-tools";
    public const string Playground = CollectionCapabilities.Playground;

    public static bool IsValid(string? id) =>
        id is PublicTechniques or CuratedTools or Playground;

    public static IActionResult? Apply(HttpRequest request, AppDbContext db, bool write = false)
    {
        var policy = PublicDemoPolicy.For(request);
        if (!CollectionRequestContext.TryResolve(request, policy, out var context))
            return new BadRequestObjectResult(new ErrorResponseDto("An explicit valid collectionId is required."));

        if (!CollectionAuthorizationPolicy.For(request).Allows(request, context!.CollectionId, write))
            return new ObjectResult(new { error = "collection_access_denied" }) { StatusCode = StatusCodes.Status403Forbidden };

        db.CollectionId = context!.CollectionId;
        db.PublicDemoRequest = policy.Enabled;
        db.PublicOnly = policy.Enabled && context.CollectionId != Playground;
        db.PublicCollectionWriteAllowed = policy.Enabled && write && context.Capabilities.CanWrite;

        if (policy.Enabled && (write
                ? !context.Capabilities.CanWrite
                : !HttpMethods.IsGet(request.Method)))
            return PublicDemoPolicy.Denied();
        if (policy.Enabled && !write &&
            System.Text.RegularExpressions.Regex.IsMatch(request.Path.Value ?? "",
                @"^/api/(tasks|workflows|methods)/[0-9a-fA-F-]{36}/(comments|rating)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) &&
            !context.Capabilities.CanReadEngagement)
            return PublicDemoPolicy.Denied();

        return null;
    }
}
