using WorkflowGallery.Api.Dtos;

namespace WorkflowGallery.Api.Services;

/// <summary>
/// Non-sensitive catalog metadata snapshot the AI model is grounded on — never inline content or
/// file data, only what is already visible via search.
/// </summary>
public record CatalogTaskSnapshot(
    Guid Id,
    string Name,
    string Description,
    string TaskType,
    IReadOnlyList<string> TargetIds,
    IReadOnlyList<string> Tags,
    string? Format = null);

/// <summary>
/// Generates a workflow proposal (task-mapping + gaps) for a user-typed goal, grounded on the
/// current task catalog.
/// </summary>
public interface IAiComposerService
{
    /// <summary>
    /// Calls the AI Foundry model deployment with <paramref name="goal"/> and
    /// <paramref name="catalog"/>, parses its structured response, and re-validates every
    /// referenced task id against <paramref name="catalog"/> before returning (FR-004) — any id
    /// that does not resolve is dropped from the steps and converted into a gap instead.
    /// Throws <see cref="AiComposerUnavailableException"/> if the model call fails, times out, or
    /// returns a response that cannot be parsed into a usable proposal after retries.
    /// </summary>
    Task<ComposeResponseDto> ComposeAsync(
        string goal,
        IReadOnlyList<CatalogTaskSnapshot> catalog,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by <see cref="IAiComposerService.ComposeAsync"/> when the AI Foundry model deployment is
/// unreachable, misconfigured, times out, or returns a response that cannot be parsed into a usable
/// proposal after retries. <c>AiComposer.cs</c> maps this to a 502 response.
/// </summary>
public class AiComposerUnavailableException : Exception
{
    public AiComposerUnavailableException(string message) : base(message)
    {
    }

    public AiComposerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Fallback registered when <c>AiFoundryEndpoint</c>/<c>AiFoundryDeploymentName</c> app settings are
/// not configured (local dev without the AI Foundry infra, or prod before the feature is promoted —
/// see infra/main.bicep's <c>enableAiComposer</c> toggle). Always fails with the same 502 path every
/// other AI Foundry outage takes, so <c>AiComposer.cs</c> needs no special-casing beyond checking
/// <c>EnableAiComposer</c> first to return a friendlier 404 in that expected case.
/// </summary>
public class UnconfiguredAiComposerService : IAiComposerService
{
    public Task<Dtos.ComposeResponseDto> ComposeAsync(
        string goal,
        IReadOnlyList<CatalogTaskSnapshot> catalog,
        CancellationToken cancellationToken = default)
    {
        throw new AiComposerUnavailableException("The AI composer is not configured in this environment.");
    }
}
