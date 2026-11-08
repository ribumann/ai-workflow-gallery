using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

/// <summary>
/// `/api/ai/compose` turns a user-typed goal into a workflow proposal grounded on the current
/// task catalog.
/// Nothing here is persisted beyond the `AiComposerRequestLog` audit row (never the goal text
/// itself) — each call is a fresh, stateless generation.
/// </summary>
public class AiComposer
{
    /// <summary>Matches the request's 1-1000 character limit.</summary>
    private const int MaxGoalLength = 1000;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiComposerService _aiComposerService;
    private readonly IAiComposerRateLimiter _rateLimiter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiComposer> _logger;
    private readonly PublicDemoPolicy _publicDemoPolicy;
    private readonly PublicComposer? _publicComposer;

    public AiComposer(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IAiComposerService aiComposerService,
        IAiComposerRateLimiter rateLimiter,
        IConfiguration configuration,
        ILogger<AiComposer> logger,
        PublicDemoPolicy? publicDemoPolicy = null,
        PublicComposer? publicComposer = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _aiComposerService = aiComposerService;
        _rateLimiter = rateLimiter;
        _configuration = configuration;
        _logger = logger;
        _publicDemoPolicy = publicDemoPolicy ?? PublicDemoPolicy.FromConfiguration(configuration);
        _publicComposer = publicComposer;
    }

    [Function("ComposeWorkflow")]
    public async Task<IActionResult> Compose(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/compose")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (_publicDemoPolicy.Enabled || PublicDemoPolicy.For(req).Enabled)
            return _publicComposer is null ? PublicDemoPolicy.Denied() : await _publicComposer.ComposeAsync(req, cancellationToken);
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        // Checked first, before ever touching IAiComposerService: this is the expected/friendly
        // path for prod before the feature is promoted (enableAiComposer=false, see
        // infra/main.bicep), distinct from the 502 path a genuine AI Foundry outage takes below.
        if (!_configuration.GetValue<bool>("EnableAiComposer"))
        {
            return new NotFoundObjectResult(new { error = "The AI composer is not available in this environment." });
        }

        ComposeRequestDto? dto;
        try
        {
            dto = await BoundedJsonBody.ReadAsync<ComposeRequestDto>(req, 8192, RequestJsonOptions, cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new ObjectResult(new { error = "Request body must be 8192 bytes or fewer." }) { StatusCode = ex.StatusCode };
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Request body must be valid JSON." });
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Goal))
        {
            return new BadRequestObjectResult(new { error = "goal is required." });
        }

        var goal = dto.Goal.Trim();
        if (goal.Length > MaxGoalLength)
        {
            return new BadRequestObjectResult(new { error = $"goal must be {MaxGoalLength} characters or fewer." });
        }

        var user = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null)
        {
            return new BadRequestObjectResult(new { error = "Could not resolve the current user." });
        }

        // Non-sensitive catalog metadata only (id/name/description/type/tool-surface/tags) — never
        // Full task contents and attached file data are not included.
        var catalog = await _dbContext.Tasks
            .OrderBy(a => a.Id)
            .Select(a => new CatalogTaskSnapshot(a.Id, a.Name, a.Description, a.TaskType, a.TargetIds, a.Tags, a.Format))
            .ToListAsync(cancellationToken);
        var candidates = Services.Catalog.ComposerCandidateSelector.Select(goal, catalog);
        var rateLimitResult = await _rateLimiter.CheckAsync(user.Id, cancellationToken);
        if (!rateLimitResult.IsAllowed)
        {
            return new ObjectResult(new ComposeRateLimitedDto(rateLimitResult.RetryAfterSeconds))
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ComposeResponseDto response;
        string? failureReason = null;

        try
        {
            response = await _aiComposerService.ComposeAsync(goal, candidates, cancellationToken);
        }
        catch (AiComposerUnavailableException ex)
        {
            stopwatch.Stop();
            failureReason = "Model request failed.";
            _logger.LogError("AI composer call failed for user {UserId}: {ErrorType}.", user.Id, ex.GetType().Name);
            await LogRequestAsync(rateLimitResult.RequestId, user.Id, startedAt, goal.Length, success: false, stopwatch.ElapsedMilliseconds, failureReason, cancellationToken);

            return new ObjectResult(new { error = "Could not get a proposal from the AI model right now. Please try again shortly." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }

        stopwatch.Stop();
        await LogRequestAsync(rateLimitResult.RequestId, user.Id, startedAt, goal.Length, success: true, stopwatch.ElapsedMilliseconds, failureReason: null, cancellationToken);

        return new OkObjectResult(response);
    }

    /// <summary>Writes one audit row per FR-014 — success/failure, duration, and goal length only,
    /// never the goal text itself.</summary>
    private async Task LogRequestAsync(
        Guid? requestId,
        Guid userId,
        DateTimeOffset requestedAt,
        int goalLength,
        bool success,
        long durationMs,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var log = requestId.HasValue
            ? await _dbContext.AiComposerRequestLogs.SingleAsync(l => l.Id == requestId.Value, cancellationToken)
            : new Models.AiComposerRequestLog { Id = Guid.NewGuid(), UserId = userId, RequestedAt = requestedAt.UtcDateTime };
        if (!requestId.HasValue) _dbContext.AiComposerRequestLogs.Add(log);
        log.GoalLength = goalLength;
        log.Success = success;
        log.DurationMs = (int)Math.Min(durationMs, int.MaxValue);
        log.FailureReason = failureReason;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
