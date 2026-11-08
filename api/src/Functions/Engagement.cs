using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

/// <summary>
/// `/api/tasks/{id}/rating`, `/api/tasks/{id}/comments`, and the `/api/workflows/...` and
/// `/api/methods/...` equivalents (issues #60, #132). One Functions class handles every subject
/// kind since the logic is identical modulo which parent table is checked for existence.
/// </summary>
public class Engagement
{
    private const int MaxCommentLength = 1000;
    private const int MaxDeviceIdLength = 100;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public Engagement(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    [Function("GetTaskRating")]
    public Task<IActionResult> GetTaskRating(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => GetRating(req, EngagementSubjectType.Task, id, cancellationToken);

    [Function("RateTask")]
    public Task<IActionResult> RateTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "tasks/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => PutRating(req, EngagementSubjectType.Task, id, cancellationToken);

    [Function("ListTaskComments")]
    public Task<IActionResult> ListTaskComments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => ListComments(req, EngagementSubjectType.Task, id, cancellationToken);

    [Function("AddTaskComment")]
    public Task<IActionResult> AddTaskComment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tasks/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => AddComment(req, EngagementSubjectType.Task, id, cancellationToken);

    [Function("GetWorkflowRating")]
    public Task<IActionResult> GetWorkflowRating(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => GetRating(req, EngagementSubjectType.Workflow, id, cancellationToken);

    [Function("RateWorkflow")]
    public Task<IActionResult> RateWorkflow(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "workflows/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => PutRating(req, EngagementSubjectType.Workflow, id, cancellationToken);

    [Function("ListWorkflowComments")]
    public Task<IActionResult> ListWorkflowComments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => ListComments(req, EngagementSubjectType.Workflow, id, cancellationToken);

    [Function("AddWorkflowComment")]
    public Task<IActionResult> AddWorkflowComment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => AddComment(req, EngagementSubjectType.Workflow, id, cancellationToken);

    [Function("GetMethodRating")]
    public Task<IActionResult> GetMethodRating(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "methods/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => GetRating(req, EngagementSubjectType.Method, id, cancellationToken);

    [Function("RateMethod")]
    public Task<IActionResult> RateMethod(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "methods/{id:guid}/rating")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => PutRating(req, EngagementSubjectType.Method, id, cancellationToken);

    [Function("ListMethodComments")]
    public Task<IActionResult> ListMethodComments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "methods/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => ListComments(req, EngagementSubjectType.Method, id, cancellationToken);

    [Function("AddMethodComment")]
    public Task<IActionResult> AddMethodComment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "methods/{id:guid}/comments")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
        => AddComment(req, EngagementSubjectType.Method, id, cancellationToken);

    private async Task<IActionResult> GetRating(
        HttpRequest req,
        EngagementSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        if (!await SubjectExistsAsync(subjectType, subjectId, cancellationToken))
        {
            return new NotFoundResult();
        }

        var deviceId = req.Query["deviceId"].ToString();
        var summary = await BuildSummaryAsync(subjectType, subjectId, deviceId, cancellationToken);
        return new OkObjectResult(summary);
    }

    private async Task<IActionResult> PutRating(
        HttpRequest req,
        EngagementSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        if (!await SubjectExistsAsync(subjectType, subjectId, cancellationToken))
        {
            return new NotFoundResult();
        }

        RatingRequestDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<RatingRequestDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new ErrorResponseDto("Request body must be valid JSON."));
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.DeviceId))
        {
            return new BadRequestObjectResult(new ErrorResponseDto("deviceId is required."));
        }

        if (dto.DeviceId.Length > MaxDeviceIdLength)
        {
            return new BadRequestObjectResult(new ErrorResponseDto($"deviceId must be {MaxDeviceIdLength} characters or fewer."));
        }

        if (dto.Value is not (1 or -1))
        {
            return new BadRequestObjectResult(new ErrorResponseDto("value must be 1 or -1."));
        }

        var deviceId = dto.DeviceId.Trim();
        var value = dto.Value.Value;

        var existing = await _dbContext.Ratings.FirstOrDefaultAsync(
            r => r.SubjectType == subjectType && r.SubjectId == subjectId && r.DeviceId == deviceId,
            cancellationToken);

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            _dbContext.Ratings.Add(new Rating
            {
                Id = Guid.NewGuid(),
                SubjectType = subjectType,
                SubjectId = subjectId,
                DeviceId = deviceId,
                Value = value,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else if (existing.Value == value)
        {
            // Clicking the same choice again toggles the vote off.
            _dbContext.Ratings.Remove(existing);
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var summary = await BuildSummaryAsync(subjectType, subjectId, deviceId, cancellationToken);
        return new OkObjectResult(summary);
    }

    private async Task<IActionResult> ListComments(
        HttpRequest req,
        EngagementSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        if (!await SubjectExistsAsync(subjectType, subjectId, cancellationToken))
        {
            return new NotFoundResult();
        }

        var items = await _dbContext.Comments
            .AsNoTracking()
            .Include(c => c.CreatedByUser)
            .Where(c => c.SubjectType == subjectType && c.SubjectId == subjectId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CommentDto(
                c.Id,
                c.Body,
                c.CreatedByUser != null ? c.CreatedByUser.DisplayName : "Unknown user",
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        return new OkObjectResult(new CommentListDto(items));
    }

    private async Task<IActionResult> AddComment(
        HttpRequest req,
        EngagementSubjectType subjectType,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        if (!await SubjectExistsAsync(subjectType, subjectId, cancellationToken))
        {
            return new NotFoundResult();
        }

        CommentRequestDto? dto;
        try
        {
            dto = await JsonSerializer.DeserializeAsync<CommentRequestDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new ErrorResponseDto("Request body must be valid JSON."));
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Body))
        {
            return new BadRequestObjectResult(new ErrorResponseDto("body is required."));
        }

        var body = dto.Body.Trim();
        if (body.Length > MaxCommentLength)
        {
            return new BadRequestObjectResult(new ErrorResponseDto($"body must be {MaxCommentLength} characters or fewer."));
        }

        var user = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            SubjectType = subjectType,
            SubjectId = subjectId,
            Body = body,
            CreatedByUserId = user!.Id,
            CreatedAt = DateTime.UtcNow,
        };

        _dbContext.Comments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ObjectResult(new CommentDto(comment.Id, comment.Body, user.DisplayName, comment.CreatedAt))
        {
            StatusCode = StatusCodes.Status201Created,
        };
    }

    private Task<bool> SubjectExistsAsync(EngagementSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken)
        => subjectType switch
        {
            EngagementSubjectType.Task => _dbContext.Tasks.AsNoTracking().AnyAsync(a => a.Id == subjectId, cancellationToken),
            EngagementSubjectType.Workflow => _dbContext.Workflows.AsNoTracking().AnyAsync(w => w.Id == subjectId, cancellationToken),
            EngagementSubjectType.Method => _dbContext.AiMethods.AsNoTracking().AnyAsync(m => m.Id == subjectId, cancellationToken),
            _ => Task.FromResult(false),
        };

    private async Task<RatingSummaryDto> BuildSummaryAsync(
        EngagementSubjectType subjectType,
        Guid subjectId,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        var votes = await _dbContext.Ratings
            .AsNoTracking()
            .Where(r => r.SubjectType == subjectType && r.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        var helpfulCount = votes.Count(v => v.Value == 1);
        var notHelpfulCount = votes.Count(v => v.Value == -1);
        var myVote = !string.IsNullOrWhiteSpace(deviceId)
            ? votes.FirstOrDefault(v => v.DeviceId == deviceId)?.Value
            : null;

        return new RatingSummaryDto(helpfulCount, notHelpfulCount, myVote);
    }
}
