using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace WorkflowGallery.Api.Services.Auth;

public static class GalleryIdentity
{
    public const string AuthenticationType = "GalleryBearer";

    public static (string TenantId, string ObjectId)? Key(HttpRequest request)
    {
        var identity = request.HttpContext.User.Identities.FirstOrDefault(i =>
            i.IsAuthenticated && i.AuthenticationType == AuthenticationType);
        if (identity is null ||
            !Guid.TryParse(identity.FindFirst("tid")?.Value, out var tenant) || tenant == Guid.Empty ||
            !Guid.TryParse(identity.FindFirst("oid")?.Value, out var user) || user == Guid.Empty)
            return null;
        return (tenant.ToString(), user.ToString());
    }

    public static string DisplayName(HttpRequest request) =>
        request.HttpContext.User.FindFirstValue("name") ?? "Gallery user";

    public static bool IsAdmin(HttpRequest request) => Key(request) is not null &&
        request.HttpContext.User.Claims.Any(c => c.Type == "roles" && c.Value is "Gallery.Admin" or "admin");
}
