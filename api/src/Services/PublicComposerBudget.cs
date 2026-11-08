using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Services;

public sealed record PublicComposerBudgetResult(string State, Guid? ReservationId = null, int RetryAfterSeconds = 0)
{
    public bool Allowed => State == "ready";
}

public interface IPublicComposerBudget
{
    Task<PublicComposerBudgetResult> CheckAsync(bool reserve, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid reservationId, CancellationToken cancellationToken);
}

/// <summary>One database-wide lock, acquired before reading, not serializable reader upgrades.
/// Never hold a transaction during inference. Uncertain/failing calls retain their charge and lease.</summary>
public sealed class PublicComposerBudget(
    AppDbContext db, PublicComposerSettings settings, ILogger<PublicComposerBudget> logger,
    TimeProvider? timeProvider = null) : IPublicComposerBudget
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<PublicComposerBudgetResult> CheckAsync(bool reserve, CancellationToken cancellationToken)
    {
        if (!settings.Ready) return new(settings.OptedIn ? "unavailable" : "disabled");
        if (!db.Database.IsSqlServer() && db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            logger.LogError("Public Composer requires a durable SQL budget store; provider unavailable.");
            return new("unavailable");
        }
        // Only retry known aborted/not-acquired transactions. Never retry an uncertain commit.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(
                    db.Database.IsSqlServer() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken);
                if (db.Database.IsSqlServer())
                    await db.Database.ExecuteSqlRawAsync("""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock @Resource = N'gallery-public-composer-budget',
                            @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 1000;
                        IF @result < 0 THROW 51000, 'Public composer budget lock unavailable.', 1;
                        """, cancellationToken);

                var now = db.Database.IsSqlServer()
                    ? DateTimeOffset.FromUnixTimeSeconds(await db.Database.SqlQueryRaw<long>(
                        "SELECT DATEDIFF_BIG(SECOND, CONVERT(datetime2, '1970-01-01'), SYSUTCDATETIME()) AS [Value]")
                        .SingleAsync(cancellationToken))
                    : _clock.GetUtcNow();
                var seconds = now.ToUnixTimeSeconds();
                var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeSeconds();
                var daily = await db.PublicComposerReservations.AsNoTracking()
                    .Where(r => r.RequestedAtUnixSeconds >= day)
                    .Select(r => r.ReservedTokens).ToListAsync(cancellationToken);
                if (daily.Count >= settings.DailyRequestLimit ||
                    daily.Sum(t => (long)t) + settings.ReservedTokens > settings.DailyTokenLimit)
                    return new("quota_exhausted", RetryAfterSeconds: (int)(day + 86400 - seconds));
                var leases = await db.PublicComposerReservations.AsNoTracking()
                    .Where(r => !r.Completed && r.LeaseExpiresAtUnixSeconds > seconds)
                    .OrderBy(r => r.LeaseExpiresAtUnixSeconds)
                    .Select(r => r.LeaseExpiresAtUnixSeconds).ToListAsync(cancellationToken);
                if (leases.Count >= settings.MaxConcurrentRequests)
                    return new("busy", RetryAfterSeconds: (int)Math.Clamp(leases[0] - seconds, 1, PublicComposerSettings.LeaseSeconds));
                if (!reserve) return new("ready");
                var id = Guid.NewGuid();
                db.PublicComposerReservations.Add(new PublicComposerReservation
                {
                    Id = id, RequestedAtUnixSeconds = seconds,
                    LeaseExpiresAtUnixSeconds = seconds + PublicComposerSettings.LeaseSeconds,
                    ReservedTokens = settings.ReservedTokens,
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new("ready", id);
            }
            catch (Exception ex) when (IsStoreError(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt < 2 && DefinitelyAborted(ex))
                {
                    logger.LogWarning("Public Composer budget contention; retry {Attempt}.", attempt + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
                    continue;
                }
                logger.LogError("Public Composer budget unavailable ({ErrorType}). No inference authorized.", ex.GetType().Name);
                return new("unavailable");
            }
        }
    }

    public async Task<bool> CompleteAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            return await db.PublicComposerReservations.Where(r => r.Id == reservationId)
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Completed, true), cancellationToken) == 1;
        }
        catch (Exception ex) when (IsStoreError(ex))
        {
            logger.LogError("Public Composer lease completion failed ({ErrorType}); charge and lease retained.", ex.GetType().Name);
            return false;
        }
    }

    private static bool IsStoreError(Exception ex) =>
        ex is DbException or DbUpdateException or InvalidOperationException or TimeoutException ||
        ex is OperationCanceledException;

    private static bool DefinitelyAborted(Exception ex) => ex is SqlException { Number: 1205 };
}
