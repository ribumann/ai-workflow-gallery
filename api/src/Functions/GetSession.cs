using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

public sealed class GetSession(PublicDemoPolicy? publicDemoPolicy = null,
    PublicComposerSettings? composerSettings = null, IPublicComposerBudget? composerBudget = null)
{
    [Function("GetSession")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "session")] HttpRequest req)
    {
        if ((publicDemoPolicy ?? PublicDemoPolicy.For(req)).Enabled)
        {
            var policy = publicDemoPolicy ?? PublicDemoPolicy.For(req);
            var state = composerSettings?.OptedIn != true ? "disabled" :
                composerSettings.Ready && composerBudget is not null
                    ? (await composerBudget.CheckAsync(reserve: false, req.HttpContext.RequestAborted)).State : "unavailable";
            var collection = CollectionRequestContext.TryResolve(req, policy, out var selected)
                ? new
                {
                    id = selected!.CollectionId,
                    label = selected.Capabilities.Label,
                    description = selected.Capabilities.Description,
                    canWrite = selected.Capabilities.CanWrite,
                    isDisposable = selected.Capabilities.IsDisposable,
                }
                : null;
            return new OkObjectResult(new
            {
                isAuthenticated = false, isAuthorized = false, displayName = (string?)null,
                publicDemo = true, composerEnabled = state == "ready", composerState = state,
                composerMaxGoalCharacters = composerSettings?.Ready == true ? composerSettings.MaxGoalCharacters : 0,
                collectionCapabilities = collection,
            });
        }
        var key = GalleryIdentity.Key(req);
        var authorization = CollectionAuthorizationPolicy.For(req);
        var hasSelection = CollectionRequestContext.TryResolve(req, PublicDemoPolicy.Private, out var privateCollection);
        if (authorization.Enabled && (!hasSelection || !authorization.Allows(req, privateCollection!.CollectionId)))
            return new ObjectResult(new { error = "collection_access_denied" }) { StatusCode = StatusCodes.Status403Forbidden };
        return key is null ? new UnauthorizedResult() : new OkObjectResult(new
        {
            isAuthenticated = true,
            isAuthorized = true,
            displayName = GalleryIdentity.DisplayName(req),
            publicDemo = false,
            collectionCapabilities = hasSelection ? new
            {
                id = privateCollection!.CollectionId,
                label = privateCollection.Capabilities.Label,
                description = privateCollection.Capabilities.Description,
                canWrite = privateCollection.Capabilities.CanWrite,
                isDisposable = privateCollection.Capabilities.IsDisposable,
            } : null,
        });
    }
}
