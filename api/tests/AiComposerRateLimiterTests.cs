using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Unit tests for <see cref="AiComposerRateLimiter"/> (T090, FR-010, issue #59) — the
/// SQL-backed per-user sliding window that <c>AiComposer.cs</c> enforces before calling the AI
/// model.</summary>
public class AiComposerRateLimiterTests
{
    [Fact]
    public async Task CheckAsync_Allows_WhenUserHasNoRecentRequests()
    {
        await using var db = TestDbContextFactory.Create();
        var limiter = new AiComposerRateLimiter(db);

        var result = await limiter.CheckAsync(Guid.NewGuid());

        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task CheckAsync_Allows_WhenUnderTheLimit()
    {
        await using var db = TestDbContextFactory.Create();
        var userId = Guid.NewGuid();
        AddRequestLogs(db, userId, count: 9, ageMinutesEach: 1);
        var limiter = new AiComposerRateLimiter(db);

        var result = await limiter.CheckAsync(userId);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_Blocks_WhenAtTheLimit_AndReturnsPositiveRetryAfter()
    {
        await using var db = TestDbContextFactory.Create();
        var userId = Guid.NewGuid();
        AddRequestLogs(db, userId, count: 10, ageMinutesEach: 1);
        var limiter = new AiComposerRateLimiter(db);

        var result = await limiter.CheckAsync(userId);

        Assert.False(result.IsAllowed);
        Assert.True(result.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task CheckAsync_Allows_WhenOldRequestsHaveAgedOutOfTheWindow()
    {
        await using var db = TestDbContextFactory.Create();
        var userId = Guid.NewGuid();
        // 10 requests, but all older than the 5-minute window — none should count.
        AddRequestLogs(db, userId, count: 10, ageMinutesEach: 10);
        var limiter = new AiComposerRateLimiter(db);

        var result = await limiter.CheckAsync(userId);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_DoesNotCountOtherUsersRequests()
    {
        await using var db = TestDbContextFactory.Create();
        var userId = Guid.NewGuid();
        AddRequestLogs(db, Guid.NewGuid(), count: 10, ageMinutesEach: 1);
        var limiter = new AiComposerRateLimiter(db);

        var result = await limiter.CheckAsync(userId);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_ReservesBeforeInference_AndCountsReservations()
    {
        await using var db = TestDbContextFactory.Create();
        var limiter = new AiComposerRateLimiter(db);
        var userId = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            var result = await limiter.CheckAsync(userId);
            Assert.True(result.IsAllowed);
            Assert.NotNull(result.RequestId);
        }
        Assert.False((await limiter.CheckAsync(userId)).IsAllowed);
        Assert.Equal(10, db.AiComposerRequestLogs.Count());
        Assert.All(db.AiComposerRequestLogs, log => Assert.False(log.Success));
    }

    [Fact]
    public async Task CheckAsync_BlocksAtGlobalLimit()
    {
        await using var db = TestDbContextFactory.Create();
        AddRequestLogs(db, Guid.NewGuid(), count: 60, ageMinutesEach: 1);
        Assert.False((await new AiComposerRateLimiter(db).CheckAsync(Guid.NewGuid())).IsAllowed);
    }

    private static void AddRequestLogs(AppDbContext db, Guid userId, int count, double ageMinutesEach)
    {
        for (var i = 0; i < count; i++)
        {
            db.AiComposerRequestLogs.Add(new AiComposerRequestLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RequestedAt = DateTime.UtcNow.AddMinutes(-ageMinutesEach),
                GoalLength = 10,
                Success = true,
                DurationMs = 100,
            });
        }

        db.SaveChanges();
    }
}
