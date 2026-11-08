using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.PublicComposerTests;

namespace WorkflowGallery.Api.Tests;

public class PublicComposerBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentConnections_AtomicallyEnforceDailyRequestAndTokenCaps(bool tokenCap)
    {
        await using var file = await DatabaseFile.Create();
        var baseline = PublicComposerSettings.Read(Config());
        var settings = baseline with
        {
            DailyRequestLimit = tokenCap ? 100 : 3,
            DailyTokenLimit = tokenCap ? baseline.ReservedTokens * 3 : 1_000_000,
            MaxConcurrentRequests = 2,
        };
        var clock = new AdvancingClock();
        // Each attempt uses its own connection/context, as independent Function instances do.
        var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            await using var db = file.Context();
            var budget = Budget(db, settings, clock);
            var result = await budget.CheckAsync(true, default);
            if (result.ReservationId is { } id) await budget.CompleteAsync(id, default);
            return result;
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(3, results.Count(r => r.Allowed));
        Assert.All(results.Where(r => !r.Allowed), r => Assert.Equal("quota_exhausted", r.State));
        await using var inspect = file.Context();
        Assert.Equal(3, await inspect.PublicComposerReservations.CountAsync());
        Assert.Equal(3L * settings.ReservedTokens, await inspect.PublicComposerReservations.SumAsync(r => (long)r.ReservedTokens));
    }

    [Fact]
    public async Task ConcurrentLeases_ExpireWithoutRefund_AndSpanUtcMidnight()
    {
        await using var file = await DatabaseFile.Create();
        var settings = PublicComposerSettings.Read(Config()) with { MaxConcurrentRequests = 2 };
        var clock = new Clock { Now = new(2026, 11, 5, 23, 59, 59, TimeSpan.Zero) };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = file.Context();
            return await Budget(db, settings, clock).CheckAsync(true, default);
        })));
        Assert.Equal(2, results.Count(r => r.Allowed));
        Assert.All(results.Where(r => !r.Allowed), r => Assert.Equal("busy", r.State));
        clock.Now = clock.Now.AddSeconds(2);
        await using var check = file.Context();
        Assert.Equal("busy", (await Budget(check, settings, clock).CheckAsync(false, default)).State);
        clock.Now = clock.Now.AddSeconds(PublicComposerSettings.LeaseSeconds);
        Assert.True((await Budget(check, settings, clock).CheckAsync(true, default)).Allowed);
        Assert.Equal(3, await check.PublicComposerReservations.CountAsync());
    }

    [Fact]
    public async Task SuccessfulCompletion_ReleasesOnlyLease_NotDailyTokens()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var baseline = PublicComposerSettings.Read(Config());
        var settings = baseline with { DailyTokenLimit = baseline.ReservedTokens };
        var budget = Budget(db, settings, new Clock());
        var result = await budget.CheckAsync(true, default);
        Assert.True(await budget.CompleteAsync(result.ReservationId!.Value, default));
        Assert.Equal("quota_exhausted", (await budget.CheckAsync(true, default)).State);
        Assert.Equal(settings.ReservedTokens, Assert.Single(db.PublicComposerReservations).ReservedTokens);
    }

    [Fact]
    public async Task BusyWriterAndMissingStore_FailClosedWithoutReservations()
    {
        await using var file = await DatabaseFile.Create();
        await using var blocker = file.Context();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await using var other = file.Context(timeout: 1);
        Assert.Equal("unavailable", (await Budget(other, PublicComposerSettings.Read(Config())).CheckAsync(true, default)).State);
        await transaction.RollbackAsync();
        Assert.Empty(await other.PublicComposerReservations.ToListAsync());
        await other.Database.ExecuteSqlRawAsync("DROP TABLE PublicComposerReservations");
        Assert.Equal("unavailable", (await Budget(other, PublicComposerSettings.Read(Config())).CheckAsync(true, default)).State);
    }

    [Fact]
    public async Task NonDurableProvider_IsNeverAnInferenceBudget()
    {
        await using var db = TestDbContextFactory.Create();
        Assert.Equal("unavailable", (await Budget(db, PublicComposerSettings.Read(Config())).CheckAsync(true, default)).State);
        Assert.Empty(db.PublicComposerReservations);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private int _calls;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)
            .AddSeconds(Interlocked.Increment(ref _calls) * (PublicComposerSettings.LeaseSeconds + 1));
    }

    private sealed class DatabaseFile : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"public-composer-{Guid.NewGuid():N}.sqlite");
        public AppDbContext Context(int timeout = 10) => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False;Default Timeout={timeout}").Options);
        public static async Task<DatabaseFile> Create()
        {
            var file = new DatabaseFile();
            await using var db = file.Context();
            await db.Database.EnsureCreatedAsync();
            return file;
        }
        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
