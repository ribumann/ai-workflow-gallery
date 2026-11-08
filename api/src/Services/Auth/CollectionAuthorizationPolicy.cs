using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WorkflowGallery.Api.Services.Auth;

/// <summary>Deployment-owned grants; callers cannot select an organization or supply membership.</summary>
public sealed class CollectionAuthorizationPolicy
{
    private static readonly object RequestKey = new();
    public static CollectionAuthorizationPolicy Legacy { get; } = new(null, new Dictionary<string, Grant>());
    private readonly Guid? _tenant;
    private readonly IReadOnlyDictionary<string, Grant> _grants;

    private CollectionAuthorizationPolicy(Guid? tenant, IReadOnlyDictionary<string, Grant> grants)
    {
        _tenant = tenant;
        _grants = grants;
    }

    public bool Enabled => _tenant.HasValue;

    public static CollectionAuthorizationPolicy Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("CollectionAuthorization");
        CheckKeys(section, "Enabled", "TenantId", "Collections");
        if (section.GetSection("Enabled").GetChildren().Any())
            throw new InvalidOperationException("CollectionAuthorization:Enabled must be a boolean value.");
        var flag = section["Enabled"];
        if (flag is not null && !bool.TryParse(flag, out _))
            throw new InvalidOperationException("CollectionAuthorization:Enabled must be true or false.");
        if (!bool.TryParse(flag, out var enabled) || !enabled)
        {
            if (section.GetChildren().Any(x => !x.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Collection grants require CollectionAuthorization:Enabled=true.");
            return Legacy;
        }

        if (PublicDemoPolicy.FromConfiguration(configuration).Enabled)
            throw new InvalidOperationException("Private collection grants cannot enable a public publisher endpoint.");
        var auth = GalleryAuthSettings.Read(configuration);
        if (!Guid.TryParse(section["TenantId"], out var tenant) || tenant == Guid.Empty ||
            auth.Tenants.Count != 1 || !auth.Admits(tenant))
            throw new InvalidOperationException("Collection authorization requires exactly one admitted Entra tenant matching TenantId.");

        var grants = new Dictionary<string, Grant>(StringComparer.Ordinal);
        foreach (var collection in section.GetSection("Collections").GetChildren())
        {
            if (!CollectionScope.IsValid(collection.Key))
                throw new InvalidOperationException("Collection authorization names an unknown collection.");
            CheckKeys(collection, "ReadRoles", "WriteRoles", "ReadGroups", "WriteGroups");
            var grant = new Grant(
                Values(collection, "ReadRoles"), Values(collection, "WriteRoles"),
                Values(collection, "ReadGroups", groups: true), Values(collection, "WriteGroups", groups: true));
            if (grant.ReadRoles.Count + grant.WriteRoles.Count + grant.ReadGroups.Count + grant.WriteGroups.Count == 0)
                throw new InvalidOperationException("A configured collection must have at least one role or group grant.");
            grants.Add(collection.Key, grant);
        }
        if (grants.Count == 0)
            throw new InvalidOperationException("Collection authorization requires explicit collection grants.");
        return new(tenant, grants);
    }

    public bool Allows(HttpRequest request, string collectionId, bool write = false)
    {
        if (!Enabled) return true;
        if (!_grants.TryGetValue(collectionId, out var grant)) return false;
        var identity = request.HttpContext.User.Identities.FirstOrDefault(x =>
            x.IsAuthenticated && x.AuthenticationType == GalleryIdentity.AuthenticationType);
        if (identity is null || GalleryIdentity.Key(request) is not { } key ||
            !Guid.TryParse(key.TenantId, out var tenant) || tenant != _tenant)
            return false;

        var canWrite = Matches(identity, "roles", grant.WriteRoles) || Matches(identity, "groups", grant.WriteGroups, groups: true);
        return canWrite || !write &&
            (Matches(identity, "roles", grant.ReadRoles) || Matches(identity, "groups", grant.ReadGroups, groups: true));
    }

    public bool HasAccess(HttpRequest request) =>
        !Enabled || _grants.Keys.Any(id => Allows(request, id));

    public void Stamp(HttpContext context) => context.Items[RequestKey] = this;

    public static CollectionAuthorizationPolicy For(HttpRequest request) =>
        request.HttpContext.Items.TryGetValue(RequestKey, out var value) && value is CollectionAuthorizationPolicy policy
            ? policy : request.HttpContext.RequestServices?.GetService<CollectionAuthorizationPolicy>() ?? Legacy;

    private static bool Matches(ClaimsIdentity identity, string type, IReadOnlySet<string> allowed, bool groups = false) =>
        identity.FindAll(type).Any(claim => allowed.Contains(
            groups && Guid.TryParse(claim.Value, out var group) ? group.ToString() : claim.Value));

    private static HashSet<string> Values(IConfigurationSection section, string name, bool groups = false)
    {
        var setting = section.GetSection(name);
        if (setting.GetChildren().Any())
            throw new InvalidOperationException($"Collection grant {name} must be a comma-separated string.");
        if (setting.Value is null) return new(StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in setting.Value.Split(',', StringSplitOptions.TrimEntries))
        {
            if (groups)
            {
                if (!Guid.TryParse(value, out var group) || group == Guid.Empty)
                    throw new InvalidOperationException("Collection group grants require nonempty group object GUIDs.");
                result.Add(group.ToString());
            }
            else
            {
                if (value.Length is < 1 or > 200 ||
                    value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
                    throw new InvalidOperationException("Collection role grants require exact nonempty app-role values.");
                result.Add(value);
            }
        }
        return result;
    }

    private static void CheckKeys(IConfigurationSection section, params string[] keys)
    {
        if (section.Value is not null || section.GetChildren().Any(x => !keys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Unknown or malformed collection authorization setting.");
    }

    private sealed record Grant(
        IReadOnlySet<string> ReadRoles, IReadOnlySet<string> WriteRoles,
        IReadOnlySet<string> ReadGroups, IReadOnlySet<string> WriteGroups);
}
