using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

public sealed class Catalog
{
    [Function("GetCatalog")]
    public IActionResult Get([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "catalog")] HttpRequest req)
    {
        if (PublicDemoPolicy.For(req).Enabled &&
            (!HttpMethods.IsGet(req.Method) || !CollectionScope.IsValid(req.Query["collectionId"].ToString())))
            return PublicDemoPolicy.Denied();
        return new OkObjectResult(CatalogRegistry.Default.Definition);
    }
}
