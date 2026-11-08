using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class PlaygroundWriteGuardTests
{
    [Fact]
    public void AdmissionBoundsActiveWritesAndResetsRollingMinuteLimit()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var guard = new PlaygroundWriteGuard(clock);
        var active = Enumerable.Range(0, PlaygroundWriteGuard.MaxActiveRequests)
            .Select(index => guard.TryEnter(out _))
            .ToArray();
        Assert.All(active, lease => Assert.NotNull(lease));
        Assert.Null(guard.TryEnter(out var activeRetry));
        Assert.Equal(1, activeRetry);

        foreach (var lease in active) lease!.Dispose();
        for (var index = 0; index < PlaygroundWriteGuard.AdmissionsPerMinute - PlaygroundWriteGuard.MaxActiveRequests; index++)
        {
            using var lease = guard.TryEnter(out _);
            Assert.NotNull(lease);
        }
        Assert.Null(guard.TryEnter(out var rateRetry));
        Assert.InRange(rateRetry, 1, 60);

        clock.Advance(TimeSpan.FromMinutes(1));
        using var afterWindow = guard.TryEnter(out _);
        Assert.NotNull(afterWindow);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
