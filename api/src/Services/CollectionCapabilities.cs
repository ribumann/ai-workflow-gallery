using Microsoft.AspNetCore.Http;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Services;

public sealed record CollectionCapabilities(
    string Id,
    string Label,
    string Description,
    bool CanWrite,
    bool CanReadEngagement,
    bool IsDisposable,
    bool IsPublicDemo)
{
    public const string Playground = "playground";

    public static readonly IReadOnlyList<(string Id, string Label, string Description)> Collections =
    [
        (CollectionScope.PublicTechniques, "Public techniques", "Original examples based on public documentation."),
        (CollectionScope.CuratedTools, "Curated tools", "Reserved for reviewed, publication-safe originals."),
        (Playground, "Playground", "Shared, public and disposable sandbox content."),
    ];

    public static CollectionCapabilities For(string id, PublicDemoPolicy policy)
    {
        var collection = Collections.SingleOrDefault(x => x.Id == id);
        if (collection == default)
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown collection.");

        var isPlayground = id == Playground;
        return new(
            id,
            collection.Label,
            collection.Description,
            !policy.Enabled || isPlayground && policy.PlaygroundWritesEnabled,
            !policy.Enabled || isPlayground,
            isPlayground,
            policy.Enabled);
    }
}

public sealed record CollectionRequestContext(string CollectionId, CollectionCapabilities Capabilities)
{
    private static readonly object RequestKey = new();

    public static bool TryResolve(HttpRequest request, PublicDemoPolicy policy, out CollectionRequestContext? context)
    {
        context = null;
        var values = request.Query["collectionId"];
        if (values.Count != 1 || !CollectionScope.IsValid(values[0])) return false;
        context = new(values[0]!, CollectionCapabilities.For(values[0]!, policy));
        if (!policy.Enabled && CollectionAuthorizationPolicy.For(request) is { Enabled: true } authorization)
            context = context with { Capabilities = context.Capabilities with { CanWrite = authorization.Allows(request, values[0]!, write: true) } };
        request.HttpContext.Items[RequestKey] = context;
        return true;
    }

    public static CollectionRequestContext? For(HttpRequest request) =>
        request.HttpContext.Items.TryGetValue(RequestKey, out var value)
            ? value as CollectionRequestContext
            : null;
}
