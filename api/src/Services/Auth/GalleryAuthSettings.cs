using Microsoft.Extensions.Configuration;

namespace WorkflowGallery.Api.Services.Auth;

public sealed record GalleryAuthSettings(string Audience, string Scope, Uri Resource, IReadOnlySet<Guid> Tenants, bool AllowMicrosoftTenant)
{
    public static readonly Guid MicrosoftTenant = Guid.Parse("72f988bf-86f1-41af-91ab-2d7cd011db47");

    public bool Admits(Guid tenant) => Tenants.Contains(tenant) && (tenant != MicrosoftTenant || AllowMicrosoftTenant);

    public string MetadataUrl => new Uri(Resource, "/api/oauth-resource-metadata").AbsoluteUri;

    public static GalleryAuthSettings Read(IConfiguration configuration)
    {
        var clientId = configuration["Entra:ApiClientId"];
        var scope = configuration["Entra:RequiredScope"];
        var tenantValues = configuration["Entra:AllowedTenantIds"]?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!Guid.TryParse(clientId, out var audience) || audience == Guid.Empty ||
            string.IsNullOrWhiteSpace(scope) || scope.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-') ||
            tenantValues is not { Length: > 0 } ||
            !Uri.TryCreate(configuration["Entra:McpResource"], UriKind.Absolute, out var resource) ||
            resource.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(resource.UserInfo) ||
            !string.IsNullOrEmpty(resource.Query) || !string.IsNullOrEmpty(resource.Fragment) ||
            resource.AbsolutePath != "/api/mcp")
            throw new InvalidOperationException("Entra API audience, scope, allowed tenant IDs and HTTPS MCP resource must be configured.");

        var tenants = new HashSet<Guid>();
        foreach (var value in tenantValues)
        {
            if (!Guid.TryParse(value, out var tenant) || tenant == Guid.Empty)
                throw new InvalidOperationException("Entra:AllowedTenantIds must contain nonempty directory GUIDs.");
            tenants.Add(tenant);
        }

        var toggle = configuration["Entra:AllowMicrosoftTenant"];
        if (toggle is not null && !bool.TryParse(toggle, out _))
            throw new InvalidOperationException("Entra:AllowMicrosoftTenant must be true or false.");
        return new(audience.ToString(), scope, resource, tenants, bool.TryParse(toggle, out var enabled) && enabled);
    }
}
