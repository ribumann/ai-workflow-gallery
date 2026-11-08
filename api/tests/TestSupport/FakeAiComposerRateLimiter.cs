using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Fake <see cref="IAiComposerRateLimiter"/>: always returns a fixed allow/deny result,
/// isolating <c>AiComposerFunctionTests</c> from the real SQL-backed window logic covered
/// separately by <c>AiComposerRateLimiterTests</c>.</summary>
public class FakeAiComposerRateLimiter : IAiComposerRateLimiter
{
    private readonly RateLimitCheckResult _result;

    private FakeAiComposerRateLimiter(RateLimitCheckResult result)
    {
        _result = result;
    }

    public static FakeAiComposerRateLimiter Allowing() => new(new RateLimitCheckResult(true, 0));

    public static FakeAiComposerRateLimiter Blocking(int retryAfterSeconds) =>
        new(new RateLimitCheckResult(false, retryAfterSeconds));

    public Task<RateLimitCheckResult> CheckAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_result);
}
