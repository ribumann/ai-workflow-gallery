using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Fake <see cref="IAiComposerService"/>: returns a fixed response, or throws
/// <see cref="AiComposerUnavailableException"/> to simulate an AI Foundry outage, bypassing any
/// real model call in tests.</summary>
public class FakeAiComposerService : IAiComposerService
{
    private readonly ComposeResponseDto? _response;

    private FakeAiComposerService(ComposeResponseDto? response)
    {
        _response = response;
    }

    public static FakeAiComposerService Succeeding(ComposeResponseDto response) => new(response);

    public static FakeAiComposerService Failing() => new(null);

    public Task<ComposeResponseDto> ComposeAsync(
        string goal,
        IReadOnlyList<CatalogTaskSnapshot> catalog,
        CancellationToken cancellationToken = default)
    {
        if (_response is null)
        {
            throw new AiComposerUnavailableException("Simulated AI Foundry outage.");
        }

        return Task.FromResult(_response);
    }
}
