using Microsoft.EntityFrameworkCore;
using System.Data;
using WorkflowGallery.Api.Data;

namespace WorkflowGallery.Api.Services;

/// <summary>
/// Reserves per-user and gallery-wide budgets for `POST /api/ai/compose`, counting
/// <see cref="Models.AiComposerRequestLog"/> rows in SQL so the budget is shared across scaled
/// Function App instances.
/// </summary>
public interface IAiComposerRateLimiter
{
    /// <summary>
    /// Atomically reserves a request when allowed; otherwise returns how long until a budget
    /// becomes available. Reservations count even when inference is interrupted.
    /// </summary>
    Task<RateLimitCheckResult> CheckAsync(Guid userId, CancellationToken cancellationToken = default);
}

public record RateLimitCheckResult(bool IsAllowed, int RetryAfterSeconds, Guid? RequestId = null);

public class AiComposerRateLimiter : IAiComposerRateLimiter
{
    /// <summary>Max requests a single user may make within <see cref="WindowDuration"/>.</summary>
    private const int MaxRequestsPerWindow = 10;

    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _dbContext;

    public AiComposerRateLimiter(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RateLimitCheckResult> CheckAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        if (transaction is not null)
        {
            // Serialize the shared budget before reading; serializable readers alone can
            // deadlock when both try to insert their reservation into the same key range.
            await _dbContext.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'gallery-ai-composer-budget',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 5000;
                IF @result < 0 THROW 51000, 'Could not reserve the AI composer request budget.', 1;
                """, cancellationToken);
        }
        var windowStart = DateTime.UtcNow - WindowDuration;

        var requestsInWindow = await _dbContext.AiComposerRequestLogs
            .Where(l => l.RequestedAt >= windowStart)
            .OrderBy(l => l.RequestedAt)
            .Select(l => new { l.UserId, l.RequestedAt })
            .ToListAsync(cancellationToken);

        var userRequests = requestsInWindow.Where(l => l.UserId == userId).ToList();
        if (userRequests.Count < MaxRequestsPerWindow && requestsInWindow.Count < 60)
        {
            var requestId = Guid.NewGuid();
            _dbContext.AiComposerRequestLogs.Add(new Models.AiComposerRequestLog
            {
                Id = requestId,
                UserId = userId,
                RequestedAt = DateTime.UtcNow,
                Success = false,
                FailureReason = "Request reserved; completion not confirmed.",
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new RateLimitCheckResult(true, 0, requestId);
        }

        var oldestInWindow = (userRequests.Count >= MaxRequestsPerWindow ? userRequests : requestsInWindow)[0].RequestedAt;
        var retryAfter = (int)Math.Ceiling((oldestInWindow + WindowDuration - DateTime.UtcNow).TotalSeconds);
        return new RateLimitCheckResult(false, Math.Max(retryAfter, 1));
    }
}
