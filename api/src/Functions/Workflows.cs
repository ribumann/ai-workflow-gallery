using System.Text.Json;
using System.Text.Json.Serialization;
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
/// `/api/workflows` endpoints for listing, reading, creating, updating, and deleting workflows.
/// </summary>
public class Workflows
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;

    /// <summary>
    /// Tolerance used when comparing a client-supplied `expectedLastEditedAt` against the stored
    /// value: DateTime round-trips through JSON/SQL Server datetime2 precisely enough that this is
    /// mostly a defensive guard against Kind/precision drift, not a real allowance for staleness.
    /// </summary>
    private static readonly TimeSpan ConcurrencyTolerance = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Options for manually parsing POST/PUT request bodies: Functions' isolated-worker model reads
    /// the raw request stream itself, so it doesn't go through the `Microsoft.AspNetCore.Mvc.JsonOptions`
    /// configured in Program.cs for outbound `IActionResult` serialization — these must match that
    /// configuration (string enums, case-insensitive property names for a lenient contract).
    /// </summary>
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public Workflows(AppDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    [Function("ListWorkflows")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        var collectionId = _dbContext.CollectionId;
        var publicOnly = _dbContext.PublicOnly;
        var query = _dbContext.Workflows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.CollectionId == collectionId && (!publicOnly || w.IsPublished))
            .AsQueryable();

        var q = req.Query["q"].ToString();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLowerInvariant();
            query = query.Where(w =>
                w.Name.ToLower().Contains(needle) ||
                w.ProblemStatement.ToLower().Contains(needle) ||
                w.DesiredOutcome.ToLower().Contains(needle));
        }

        if (Guid.TryParse(req.Query["taskId"], out var taskId))
        {
            query = query.Where(w => w.Steps.Any(s =>
                s.TaskId == taskId &&
                (!publicOnly || (s.Task != null && s.Task.CollectionId == collectionId && s.Task.IsPublished))));
        }

        var taskType = req.Query["taskType"].ToString();
        if (req.Query.ContainsKey("taskType"))
        {
            if (!CatalogRegistry.Default.IsKind(taskType)) return BadRequest("Unknown task type.");
            query = query.Where(w => w.Steps.Any(s =>
                (s.Task != null && s.Task.CollectionId == collectionId &&
                    (!publicOnly || s.Task.IsPublished) && s.Task.TaskType == taskType) ||
                (!publicOnly && s.Task == null && s.TaskTypeSnapshot == taskType)));
        }

        var targetId = req.Query["targetId"].ToString();
        if (req.Query.ContainsKey("targetId"))
        {
            if (!CatalogRegistry.Default.IsTarget(targetId)) return BadRequest("Unknown target ID.");
            query = query.Where(w => w.Steps.Any(s => s.TargetId == targetId &&
                (!publicOnly || (s.Task != null && s.Task.CollectionId == collectionId && s.Task.IsPublished))));
        }

        var (page, pageSize) = ParsePaging(req);
        if ((long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest("Page exceeds the supported paging range.");

        var totalCount = await query.CountAsync(cancellationToken);
        var workflows = await query
            .OrderByDescending(w => w.LastEditedAt)
            .ThenBy(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.ProblemStatement,
                w.DesiredOutcome,
                CreatedByDisplayName = w.CreatedByUser == null ? "Unknown user" : w.CreatedByUser.DisplayName,
                w.LastEditedAt,
            })
            .ToListAsync(cancellationToken);

        var workflowIds = workflows.Select(w => w.Id).ToList();
        var stepRows = new List<(Guid WorkflowId, int SequenceOrder, string? TargetId, bool HasVisibleTask)>();
        if (workflowIds.Count > 0)
        {
            var queriedSteps = await _dbContext.WorkflowSteps
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => workflowIds.Contains(s.WorkflowId) &&
                    s.Workflow != null &&
                    s.Workflow.CollectionId == collectionId &&
                    (!publicOnly || s.Workflow.IsPublished))
                .Select(s => new
                {
                    s.WorkflowId,
                    s.SequenceOrder,
                    s.TargetId,
                    HasVisibleTask = s.Task != null &&
                        s.Task.CollectionId == collectionId &&
                        (!publicOnly || s.Task.IsPublished),
                })
                .ToListAsync(cancellationToken);
            stepRows = queriedSteps
                .Select(s => (s.WorkflowId, s.SequenceOrder, s.TargetId, s.HasVisibleTask))
                .ToList();
        }
        var stepsByWorkflow = stepRows.GroupBy(s => s.WorkflowId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var items = workflows.Select(w =>
        {
            var steps = stepsByWorkflow.GetValueOrDefault(w.Id) ?? new List<(Guid WorkflowId, int SequenceOrder, string? TargetId, bool HasVisibleTask)>();
            var targets = steps
                .Where(s => s.TargetId != null && (!publicOnly || s.HasVisibleTask))
                .Select(s => s.TargetId!)
                .Distinct()
                .ToList();
            return new WorkflowSummaryDto(
                w.Id,
                w.Name,
                w.ProblemStatement,
                w.DesiredOutcome,
                steps.Count,
                targets,
                w.CreatedByDisplayName,
                w.LastEditedAt);
        }).ToList();

        return new OkObjectResult(new PagedResultDto<WorkflowSummaryDto>(items, totalCount, page, pageSize));
    }

    [Function("GetWorkflow")]
    public virtual async Task<IActionResult> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workflows/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        var workflow = await LoadReadDetailAsync(id, cancellationToken);
        return workflow is null ? new NotFoundResult() : new OkObjectResult(workflow);
    }

    [Function("CreateWorkflow")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workflows")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        CreateWorkflowRequestDto? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateWorkflowRequestDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest("The request body is not valid JSON.");
        }

        if (body is null)
        {
            return BadRequest("A request body is required.");
        }

        var validationError = ValidateInput(body.Name, body.ProblemStatement, body.DesiredOutcome, body.Steps);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var missingTaskError = await ValidateTasksExistAsync(body.Steps!, cancellationToken);
        if (missingTaskError is not null)
        {
            return BadRequest(missingTaskError);
        }

        var association = await MethodAssociation.ResolveAsync(_dbContext, body.MethodId, cancellationToken);
        if (association.Error is not null) return BadRequest(association.Error);

        var user = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null)
        {
            return new UnauthorizedResult();
        }

        var now = DateTime.UtcNow;
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = body.Name!.Trim(),
            ProblemStatement = body.ProblemStatement!.Trim(),
            DesiredOutcome = body.DesiredOutcome!.Trim(),
            CreatedByUserId = user.Id,
            CreatedAt = now,
            LastEditedByUserId = user.Id,
            LastEditedAt = now,
            MethodId = association.Method?.Id,
        };
        workflow.Steps = BuildSteps(workflow.Id, body.Steps!);

        _dbContext.Workflows.Add(workflow);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (MethodAssociation.IsReferenceViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return BadRequest(MethodAssociation.MissingMessage);
        }

        var created = await LoadForDetailAsync(workflow.Id, cancellationToken);
        return new ObjectResult(ToDetailDto(created!)) { StatusCode = StatusCodes.Status201Created };
    }

    [Function("UpdateWorkflow")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "workflows/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        UpdateWorkflowRequestDto? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<UpdateWorkflowRequestDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return BadRequest("The request body is not valid JSON.");
        }

        if (body is null)
        {
            return BadRequest("A request body is required.");
        }

        var validationError = ValidateInput(body.Name, body.ProblemStatement, body.DesiredOutcome, body.Steps);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var user = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null)
        {
            return new UnauthorizedResult();
        }

        try
        {
            // Keep issue #67's two-save replacement, but commit both saves atomically. The SQL
            // retry strategy owns the transaction; every retry reloads state and rebuilds steps.
            return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                await using var transaction = _dbContext.Database.IsRelational()
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
                var workflow = await _dbContext.Workflows.Include(w => w.Steps)
                    .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
                if (workflow is null) return (IActionResult)new NotFoundResult();
                if (body.ExpectedLastEditedAt is { } expected &&
                    (workflow.LastEditedAt - expected).Duration() > ConcurrencyTolerance)
                {
                    var current = await LoadForDetailAsync(id, cancellationToken);
                    return new ConflictObjectResult(ToDetailDto(current!));
                }
                var missingTaskError = await ValidateTasksExistAsync(body.Steps!, cancellationToken);
                if (missingTaskError is not null) return BadRequest(missingTaskError);
                var association = await MethodAssociation.ResolveAsync(_dbContext, body.MethodId, cancellationToken);
                if (association.Error is not null) return BadRequest(association.Error);

                workflow.Name = body.Name!.Trim();
                workflow.ProblemStatement = body.ProblemStatement!.Trim();
                workflow.DesiredOutcome = body.DesiredOutcome!.Trim();
                workflow.LastEditedByUserId = user.Id;
                workflow.LastEditedAt = DateTime.UtcNow;
                if (association.Specified) workflow.MethodId = association.Method?.Id;

                // Flush deletions before inserting reused sequence numbers, without reassigning
                // the tracked required collection (the production fix for issue #67).
                _dbContext.WorkflowSteps.RemoveRange(workflow.Steps);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _dbContext.WorkflowSteps.AddRange(BuildSteps(workflow.Id, body.Steps!));
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);

                var updated = await LoadForDetailAsync(id, cancellationToken);
                return new OkObjectResult(ToDetailDto(updated!));
            });
        }
        catch (DbUpdateException ex) when (MethodAssociation.IsReferenceViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return BadRequest(MethodAssociation.MissingMessage);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await LoadForDetailAsync(id, cancellationToken);
            return current is null ? new NotFoundResult() : new ConflictObjectResult(ToDetailDto(current));
        }
    }

    [Function("DeleteWorkflow")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "workflows/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        var workflow = await _dbContext.Workflows.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workflow is null)
        {
            return new NotFoundResult();
        }

        // WorkflowStep rows cascade-delete via the FK configured in AppDbContext.
        _dbContext.Workflows.Remove(workflow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new NoContentResult();
    }

    /// <summary>
    /// Shared by every route returning a full Workflow record (GetById, T020; Create/Update, T038/
    /// T039) so the query shape used to resolve steps' live-vs-snapshot task info is never
    /// duplicated.
    /// </summary>
    private Task<Workflow?> LoadForDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Workflows
            .AsNoTracking()
            .Include(w => w.CreatedByUser)
            .Include(w => w.LastEditedByUser)
            .Include(w => w.Method)
            .Include(w => w.Steps.OrderBy(s => s.SequenceOrder))
            .ThenInclude(s => s.Task)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    private async Task<WorkflowDetailDto?> LoadReadDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var collectionId = _dbContext.CollectionId;
        var publicOnly = _dbContext.PublicOnly;
        var workflow = await _dbContext.Workflows
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.Id == id && w.CollectionId == collectionId && (!publicOnly || w.IsPublished))
            .Select(w => new WorkflowDetailDto(
                w.Id,
                w.Name,
                w.ProblemStatement,
                w.DesiredOutcome,
                new List<WorkflowStepDto>(),
                new UserRefDto(w.CreatedByUserId, w.CreatedByUser == null ? "Unknown user" : w.CreatedByUser.DisplayName),
                w.CreatedAt,
                new UserRefDto(w.LastEditedByUserId, w.LastEditedByUser == null ? "Unknown user" : w.LastEditedByUser.DisplayName),
                w.LastEditedAt,
                publicOnly && (w.Method == null || w.Method.CollectionId != collectionId || !w.Method.IsPublished)
                    ? null
                    : w.MethodId,
                w.Method != null && w.Method.CollectionId == collectionId &&
                    (!publicOnly || w.Method.IsPublished)
                    ? new MethodRefDto(w.Method.Id, w.Method.Title, w.Method.Summary)
                    : null))
            .SingleOrDefaultAsync(cancellationToken);

        if (workflow is null) return null;

        var steps = await _dbContext.WorkflowSteps
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.WorkflowId == id &&
                s.Workflow != null &&
                s.Workflow.CollectionId == collectionId &&
                (!publicOnly || s.Workflow.IsPublished))
            .OrderBy(s => s.SequenceOrder)
            .Select(s => new
            {
                s.SequenceOrder,
                s.TaskId,
                s.IsManualStep,
                s.ManualStepTitle,
                s.ManualStepInstructions,
                s.TargetId,
                s.Notes,
                s.DataSource,
                s.DataOutput,
                s.TaskNameSnapshot,
                s.TaskTypeSnapshot,
                TaskIsVisible = s.Task != null &&
                    s.Task.CollectionId == collectionId &&
                    (!publicOnly || s.Task.IsPublished),
                TaskName = s.Task == null ? null : s.Task.Name,
                TaskType = s.Task == null ? null : s.Task.TaskType,
                TaskTargetIds = s.Task == null ? new List<string>() : s.Task.TargetIds,
            })
            .ToListAsync(cancellationToken);
        var detailSteps = steps.Select(s => new WorkflowStepDto(
            s.SequenceOrder,
            publicOnly && !s.TaskIsVisible ? null : s.TaskId,
            publicOnly ? s.TaskIsVisible : s.TaskId is not null,
            s.IsManualStep
                ? s.ManualStepTitle ?? "Manual step"
                : s.TaskIsVisible
                    ? s.TaskName ?? "Unknown task"
                    : publicOnly ? "Unavailable task" : s.TaskNameSnapshot ?? "Unknown task",
            s.IsManualStep
                ? null
                : s.TaskIsVisible
                    ? s.TaskType
                    : publicOnly ? null : s.TaskTypeSnapshot,
            s.IsManualStep || publicOnly && !s.TaskIsVisible ? null : s.TargetId,
            s.Notes,
            s.IsManualStep,
            s.ManualStepInstructions,
            s.DataSource,
            s.DataOutput,
            s.IsManualStep || !s.TaskIsVisible ? new List<string>() : s.TaskTargetIds))
            .ToList();

        return workflow with { Steps = detailSteps };
    }

    /// <summary>Builds the ordered WorkflowStep rows for a create/update request (FR-008).</summary>
    public static List<WorkflowStep> BuildSteps(Guid workflowId, List<WorkflowStepInputDto> steps)
    {
        return steps
            .Select((s, index) => new WorkflowStep
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                SequenceOrder = index + 1,
                TaskId = s.IsManualStep ? null : s.TaskId,
                TargetId = s.TargetId,
                Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim(),
                IsManualStep = s.IsManualStep,
                ManualStepTitle = s.IsManualStep ? s.ManualStepTitle!.Trim() : null,
                ManualStepInstructions = s.IsManualStep && !string.IsNullOrWhiteSpace(s.ManualStepInstructions)
                    ? s.ManualStepInstructions.Trim()
                    : null,
                DataSource = string.IsNullOrWhiteSpace(s.DataSource) ? null : s.DataSource.Trim(),
                DataOutput = string.IsNullOrWhiteSpace(s.DataOutput) ? null : s.DataOutput.Trim(),
            })
            .ToList();
    }

    /// <summary>
    /// Validates the shared create/update fields (FR-006, FR-012): required text fields and the
    /// at-least-2-steps rule, with an explanatory message for the latter per FR-012. Also validates
    /// each step is either task-backed (a valid TaskId) or a manual step (a non-blank
    /// ManualStepTitle) per issue #67's "manual step" feature.
    /// </summary>
    public static string? ValidateInput(
        string? name,
        string? problemStatement,
        string? desiredOutcome,
        List<WorkflowStepInputDto>? steps)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(problemStatement))
        {
            return "Problem statement is required.";
        }

        if (string.IsNullOrWhiteSpace(desiredOutcome))
        {
            return "Desired outcome is required.";
        }

        if (steps is null || steps.Count < 2)
        {
            return "A workflow must have at least 2 steps before it can be saved.";
        }

        foreach (var step in steps)
        {
            if (step.IsManualStep)
            {
                if (step.TaskId is not null || step.TargetId is not null)
                    return "Manual steps must have null taskId and targetId.";
                if (string.IsNullOrWhiteSpace(step.ManualStepTitle))
                {
                    return "Every manual step must have a title.";
                }
            }
            else if (step.TaskId is null)
            {
                return "Every step must reference a task or be marked as a manual step.";
            }
        }

        return null;
    }

    /// <summary>Validates every referenced taskId exists for POST and PUT requests.
    /// Manual steps (no TaskId) are skipped — there is nothing to look up.</summary>
    private async Task<string?> ValidateTasksExistAsync(List<WorkflowStepInputDto> steps, CancellationToken cancellationToken)
    {
        var taskIds = steps
            .Where(s => !s.IsManualStep && s.TaskId is not null)
            .Select(s => s.TaskId!.Value)
            .Distinct()
            .ToList();

        if (taskIds.Count == 0)
        {
            return null;
        }

        var tasks = await _dbContext.Tasks
            .Where(a => taskIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var missing = taskIds.Except(tasks.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
        {
            return $"The following task(s) do not exist: {string.Join(", ", missing)}.";
        }

        foreach (var step in steps)
        {
            var error = CatalogRegistry.Default.ValidateStep(tasks.FirstOrDefault(t => t.Id == step.TaskId), step.TargetId, step.IsManualStep);
            if (error is not null) return error;
        }
        return null;
    }

    private static BadRequestObjectResult BadRequest(string message) =>
        new(new ErrorResponseDto(message));

    /// <summary>
    /// Shared by GetById (T020) and the create/update handlers added by US3 so every route
    /// returning a full Workflow record uses the exact same shape.
    /// </summary>
    internal static WorkflowDetailDto ToDetailDto(Workflow workflow, bool publicOnly = false)
    {
        var steps = workflow.Steps
            .OrderBy(s => s.SequenceOrder)
            .Select(s => new WorkflowStepDto(
                s.SequenceOrder,
                publicOnly && s.Task is null ? null : s.TaskId,
                publicOnly ? s.Task is not null : s.TaskId is not null,
                // Live task while it still exists; last-known snapshot once deleted (FR-010) —
                // the client never has to special-case the deleted state to render a step. Manual
                // steps (issue #67) have no task at all, so their title stands in here instead.
                s.IsManualStep
                    ? s.ManualStepTitle ?? "Manual step"
                    : s.Task?.Name ?? (publicOnly ? "Unavailable task" : s.TaskNameSnapshot ?? "Unknown task"),
                s.IsManualStep ? null : s.Task?.TaskType ?? (publicOnly ? null : s.TaskTypeSnapshot),
                s.IsManualStep || (publicOnly && s.Task is null) ? null : s.TargetId,
                s.Notes,
                s.IsManualStep,
                s.ManualStepInstructions,
                s.DataSource,
                s.DataOutput,
                s.IsManualStep ? new List<string>() : s.Task?.TargetIds.ToList() ?? new List<string>()))
            .ToList();

        return new WorkflowDetailDto(
            workflow.Id,
            workflow.Name,
            workflow.ProblemStatement,
            workflow.DesiredOutcome,
            steps,
            new UserRefDto(workflow.CreatedByUserId, workflow.CreatedByUser?.DisplayName ?? "Unknown user"),
            workflow.CreatedAt,
            new UserRefDto(workflow.LastEditedByUserId, workflow.LastEditedByUser?.DisplayName ?? "Unknown user"),
            workflow.LastEditedAt,
            publicOnly && workflow.Method is null ? null : workflow.MethodId,
            MethodAssociation.ToDto(workflow.Method));
    }

    private static (int Page, int PageSize) ParsePaging(HttpRequest req)
    {
        var page = int.TryParse(req.Query["page"], out var p) && p > 0 ? p : DefaultPage;
        var pageSize = int.TryParse(req.Query["pageSize"], out var ps) && ps > 0 ? Math.Min(ps, 100) : DefaultPageSize;
        return (page, pageSize);
    }
}
