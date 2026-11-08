using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

/// <summary>
/// `/api/tasks` endpoints for listing, reading, creating, editing, deleting, and downloading tasks.
/// </summary>
public class Tasks
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;

    /// <summary>Allowed clock skew when comparing `expectedLastEditedAt` against the stored
    /// timestamp, to absorb any sub-millisecond precision loss across JSON round-tripping.</summary>
    private static readonly TimeSpan ConcurrencyComparisonTolerance = TimeSpan.FromMilliseconds(1);

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IFileValidationService _fileValidationService;
    private readonly ILogger<Tasks> _logger;

    public Tasks(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IBlobStorageService blobStorageService,
        IFileValidationService fileValidationService,
        ILogger<Tasks>? logger = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _blobStorageService = blobStorageService;
        _fileValidationService = fileValidationService;
        _logger = logger ?? NullLogger<Tasks>.Instance;
    }

    [Function("ListTasks")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        var query = _dbContext.Tasks.AsNoTracking().AsQueryable();

        var type = req.Query["type"].ToString();
        if (req.Query.ContainsKey("type"))
        {
            if (!CatalogRegistry.Default.IsKind(type))
                return new BadRequestObjectResult(new ErrorResponseDto("Unknown task type."));
            query = query.Where(a => a.TaskType == type);
        }

        var targetId = req.Query["targetId"].ToString();
        if (req.Query.ContainsKey("targetId") && !CatalogRegistry.Default.IsTarget(targetId))
            return new BadRequestObjectResult(new ErrorResponseDto("Unknown target ID."));

        var (page, pageSize) = ParsePaging(req);
        if ((long)(page - 1) * pageSize > int.MaxValue)
            return new BadRequestObjectResult(new ErrorResponseDto("Page exceeds the supported paging range."));

        // `Tags` is stored as a comma-joined string via a value converter (AppDbContext), so EF
        // Core cannot translate a per-tag `.ToLower().Contains(...)` predicate to SQL — it throws
        // at request time ("The LINQ expression ... could not be translated"). The `q` filter is
        // therefore applied client-side, after the SQL-translatable type/toolSurface filters, which
        // is safe at this app's documented scale (spec.md: ~2,000 tasks tolerated comfortably).
        var q = req.Query["q"].ToString();
        IEnumerable<TaskSummaryDto> filtered = await query.Select(a => new TaskSummaryDto(
            a.Id, a.Name, a.Description, a.TaskType, a.TargetIds, a.Tags,
            a.ReferenceLink != null, a.InlineContent != null, a.AttachedFileBlobPath != null,
            a.CreatedByUser != null ? a.CreatedByUser.DisplayName : "Unknown user",
            a.LastEditedAt, a.Format, a.Setup, a.Prerequisites)).ToListAsync(cancellationToken);
        if (req.Query.ContainsKey("targetId"))
            filtered = filtered.Where(a => a.TargetIds.Contains(targetId, StringComparer.Ordinal));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            filtered = filtered.Where(a =>
                a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                a.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        var ordered = filtered.OrderByDescending(a => a.LastEditedAt).ThenBy(a => a.Id).ToList();
        var totalCount = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new OkObjectResult(new PagedResultDto<TaskSummaryDto>(items, totalCount, page, pageSize));
    }

    [Function("GetTask")]
    public async Task<IActionResult> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        var task = await _dbContext.Tasks
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new TaskDetailDto(
                a.Id,
                a.Name,
                a.Description,
                a.TaskType,
                a.TargetIds,
                a.Tags,
                a.ReferenceLink,
                a.InlineContent,
                a.DataSource,
                a.DataOutput,
                a.AttachedFileBlobPath == null ? null : new TaskFileDto(
                    a.AttachedFileName ?? "file",
                    a.AttachedFileSizeBytes ?? 0,
                    $"/api/tasks/{a.Id}/file?collectionId={a.CollectionId}"),
                new UserRefDto(a.CreatedByUserId, a.CreatedByUser == null ? "Unknown user" : a.CreatedByUser.DisplayName),
                a.CreatedAt,
                new UserRefDto(a.LastEditedByUserId, a.LastEditedByUser == null ? "Unknown user" : a.LastEditedByUser.DisplayName),
                a.LastEditedAt,
                new List<Guid>(),
                _dbContext.PublicOnly && a.Method == null ? null : a.MethodId,
                a.Method == null ? null : new MethodRefDto(a.Method.Id, a.Method.Title, a.Method.Summary),
                a.Format,
                a.Setup,
                a.Prerequisites))
            .SingleOrDefaultAsync(cancellationToken);

        if (task is null)
        {
            return new NotFoundResult();
        }

        var referencedByWorkflowIds = await _dbContext.WorkflowSteps
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TaskId == id &&
                s.Workflow != null &&
                s.Workflow.CollectionId == _dbContext.CollectionId &&
                (!_dbContext.PublicOnly || s.Workflow.IsPublished))
            .Select(s => s.WorkflowId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new OkObjectResult(task with { ReferencedByWorkflowIds = referencedByWorkflowIds });
    }

    [Function("CreateTask")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tasks")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        var currentUser = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (currentUser is null)
        {
            return new UnauthorizedObjectResult(new ErrorResponseDto("Sign-in required."));
        }

        if (!req.HasFormContentType)
        {
            return new BadRequestObjectResult(new ErrorResponseDto("Request must be multipart/form-data."));
        }

        var form = await req.ReadFormAsync(cancellationToken);
        if (!TryParseMetadata(form, out var metadata, out var parseError))
        {
            return new BadRequestObjectResult(new ErrorResponseDto(parseError!));
        }

        var file = form.Files["file"];

        var fieldError = ValidateFields(metadata, hasFile: file is not null);
        if (fieldError is not null)
        {
            return new BadRequestObjectResult(new ErrorResponseDto(fieldError));
        }

        var association = await MethodAssociation.ResolveAsync(_dbContext, metadata.MethodId, cancellationToken);
        if (association.Error is not null)
            return new BadRequestObjectResult(new ErrorResponseDto(association.Error));

        if (file is not null)
        {
            var fileValidation = _fileValidationService.Validate(file.FileName, file.Length);
            if (!fileValidation.IsValid)
            {
                return new BadRequestObjectResult(new ErrorResponseDto(fileValidation.ErrorMessage!));
            }
        }

        var now = DateTime.UtcNow;
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = metadata.Name!.Trim(),
            Description = metadata.Description!.Trim(),
            TaskType = metadata.TaskType!,
            TargetIds = metadata.TargetIds!.ToList(),
            Format = metadata.Format,
            Setup = NullIfBlank(metadata.Setup),
            Prerequisites = NullIfBlank(metadata.Prerequisites),
            Tags = NormalizeTags(metadata.Tags),
            ReferenceLink = NullIfBlank(metadata.ReferenceLink),
            InlineContent = NullIfBlank(metadata.InlineContent),
            DataSource = NullIfBlank(metadata.DataSource),
            DataOutput = NullIfBlank(metadata.DataOutput),
            CreatedByUser = currentUser,
            CreatedByUserId = currentUser.Id,
            CreatedAt = now,
            LastEditedByUser = currentUser,
            LastEditedByUserId = currentUser.Id,
            LastEditedAt = now,
            MethodId = association.Method?.Id,
            Method = association.Method,
        };

        if (file is not null)
        {
            await using var stream = file.OpenReadStream();
            var contentType = _fileValidationService.GetContentType(file.FileName);
            task.AttachedFileBlobPath = await _blobStorageService.UploadAsync(
                _dbContext.CollectionId, task.Id, file.FileName, stream, contentType, cancellationToken);
            task.AttachedFileName = file.FileName;
            task.AttachedFileSizeBytes = file.Length;
        }

        _dbContext.Tasks.Add(task);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsConfirmedSaveRejection(ex))
        {
            _dbContext.ChangeTracker.Clear();
            if (task.AttachedFileBlobPath is not null)
                await CleanRejectedUploadAsync(task.AttachedFileBlobPath, cancellationToken);
            if (MethodAssociation.IsReferenceViolation(ex))
                return new BadRequestObjectResult(new ErrorResponseDto(MethodAssociation.MissingMessage));
            throw;
        }

        return new CreatedResult($"/api/tasks/{task.Id}?collectionId={task.CollectionId}", ToDetailDto(task, new List<Guid>()));
    }

    [Function("UpdateTask")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "tasks/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        var currentUser = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (currentUser is null)
        {
            return new UnauthorizedObjectResult(new ErrorResponseDto("Sign-in required."));
        }

        if (!req.HasFormContentType)
        {
            return new BadRequestObjectResult(new ErrorResponseDto("Request must be multipart/form-data."));
        }

        var task = await _dbContext.Tasks
            .Include(a => a.CreatedByUser)
            .Include(a => a.LastEditedByUser)
            .Include(a => a.Method)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (task is null)
        {
            return new NotFoundResult();
        }

        var form = await req.ReadFormAsync(cancellationToken);
        if (!TryParseMetadata(form, out var metadata, out var parseError))
        {
            return new BadRequestObjectResult(new ErrorResponseDto(parseError!));
        }

        // The optimistic-concurrency guard is skipped when the client omits
        // `expectedLastEditedAt`, allowing last-write-wins behavior.
        if (metadata.ExpectedLastEditedAt is { } expected &&
            (task.LastEditedAt - expected).Duration() > ConcurrencyComparisonTolerance)
        {
            var currentReferencedByWorkflowIds = await GetReferencedByWorkflowIdsAsync(id, cancellationToken);
            return new ConflictObjectResult(ToDetailDto(task, currentReferencedByWorkflowIds));
        }

        var file = form.Files["file"];
        var willHaveFile = file is not null || task.AttachedFileBlobPath is not null;

        var fieldError = ValidateFields(metadata, hasFile: willHaveFile);
        if (fieldError is not null)
        {
            return new BadRequestObjectResult(new ErrorResponseDto(fieldError));
        }

        var referencedTargets = await _dbContext.WorkflowSteps.Where(s => s.TaskId == id)
            .Select(s => s.TargetId).ToListAsync(cancellationToken);
        if (referencedTargets.Any(target => target is null || !metadata.TargetIds!.Contains(target, StringComparer.Ordinal) ||
            !CatalogRegistry.Default.Supports(metadata.TaskType, target, metadata.Format)))
            return new BadRequestObjectResult(new ErrorResponseDto("This edit would invalidate an existing workflow step target."));

        var association = await MethodAssociation.ResolveAsync(_dbContext, metadata.MethodId, cancellationToken);
        if (association.Error is not null)
            return new BadRequestObjectResult(new ErrorResponseDto(association.Error));

        if (file is not null)
        {
            var fileValidation = _fileValidationService.Validate(file.FileName, file.Length);
            if (!fileValidation.IsValid)
            {
                return new BadRequestObjectResult(new ErrorResponseDto(fileValidation.ErrorMessage!));
            }
        }

        task.Name = metadata.Name!.Trim();
        task.Description = metadata.Description!.Trim();
        task.TaskType = metadata.TaskType!;
        task.TargetIds = metadata.TargetIds!.ToList();
        task.Format = metadata.Format;
        task.Setup = NullIfBlank(metadata.Setup);
        task.Prerequisites = NullIfBlank(metadata.Prerequisites);
        task.Tags = NormalizeTags(metadata.Tags);
        task.ReferenceLink = NullIfBlank(metadata.ReferenceLink);
        task.InlineContent = NullIfBlank(metadata.InlineContent);
        task.DataSource = NullIfBlank(metadata.DataSource);
        task.DataOutput = NullIfBlank(metadata.DataOutput);
        task.LastEditedByUser = currentUser;
        task.LastEditedByUserId = currentUser.Id;
        task.LastEditedAt = DateTime.UtcNow;
        if (association.Specified)
        {
            task.Method = association.Method;
            task.MethodId = association.Method?.Id;
        }

        var previousBlobPath = task.AttachedFileBlobPath;
        if (file is not null)
        {
            await using var stream = file.OpenReadStream();
            var contentType = _fileValidationService.GetContentType(file.FileName);
            task.AttachedFileBlobPath = await _blobStorageService.UploadAsync(
                _dbContext.CollectionId, task.Id, file.FileName, stream, contentType, cancellationToken);
            task.AttachedFileName = file.FileName;
            task.AttachedFileSizeBytes = file.Length;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsConfirmedSaveRejection(ex))
        {
            _dbContext.ChangeTracker.Clear();
            if (file is not null && task.AttachedFileBlobPath is not null && task.AttachedFileBlobPath != previousBlobPath)
                await CleanRejectedUploadAsync(task.AttachedFileBlobPath, cancellationToken);
            if (MethodAssociation.IsReferenceViolation(ex))
                return new BadRequestObjectResult(new ErrorResponseDto(MethodAssociation.MissingMessage));
            throw;
        }
        if (file is not null && previousBlobPath is not null && previousBlobPath != task.AttachedFileBlobPath)
            await _blobStorageService.DeleteAsync(_dbContext.CollectionId, previousBlobPath, cancellationToken);

        var referencedByWorkflowIds = await GetReferencedByWorkflowIdsAsync(id, cancellationToken);
        return new OkObjectResult(ToDetailDto(task, referencedByWorkflowIds));
    }

    private static bool IsConfirmedSaveRejection(DbUpdateException exception)
    {
        if (exception is DbUpdateConcurrencyException) return true;
        if (exception.InnerException is not SqlException sql) return false;
        // Constraint/null/truncation rejections have a known failed statement outcome. Do not
        // clean uploads for timeouts, transport failures or other uncertain commit outcomes.
        var numbers = sql.Errors.Cast<SqlError>().Select(error => error.Number).ToArray();
        return numbers.Any(number => number != 3621) &&
            numbers.All(number => number is 515 or 547 or 2601 or 2627 or 8152 or 2628 or 3621 ||
                number is >= 51000 and <= 51099);
    }

    private async Task CleanRejectedUploadAsync(string stagedPath, CancellationToken cancellationToken)
    {
        try
        {
            // A retry may report duplicate-key/concurrency after an earlier attempt committed.
            // Keep any upload already referenced by the database, irrespective of tracked state.
            if (await _dbContext.Tasks.AsNoTracking()
                .AnyAsync(task => task.AttachedFileBlobPath == stagedPath, cancellationToken))
                return;
            await _blobStorageService.DeleteAsync(_dbContext.CollectionId, stagedPath, cancellationToken);
        }
        catch (Exception cleanupError) when (cleanupError is SqlException or RetryLimitExceededException
            or RequestFailedException or OperationCanceledException)
        {
            _logger.LogWarning(cleanupError,
                "Could not confirm or clean rejected staged attachment {BlobPath}; retaining the original database error.",
                stagedPath);
        }
    }

    [Function("DeleteTask")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "tasks/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext, write: true) is { } scopeError) return scopeError;
        var task = await _dbContext.Tasks.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (task is null)
        {
            return new NotFoundResult();
        }

        var referencingSteps = await _dbContext.WorkflowSteps
            .Where(s => s.TaskId == id)
            .Include(s => s.Workflow)
            .ToListAsync(cancellationToken);

        var confirmed = string.Equals(req.Query["confirm"], "true", StringComparison.OrdinalIgnoreCase);

        if (referencingSteps.Count > 0 && !confirmed)
        {
            var referencedByWorkflows = referencingSteps
                .Select(s => s.Workflow!)
                .DistinctBy(w => w.Id)
                .Select(w => new WorkflowRefDto(w.Id, w.Name))
                .ToList();

            return new ConflictObjectResult(new DeleteConflictDto(referencedByWorkflows));
        }

        // FR-010: referencing steps survive with TaskId=null and a name/type snapshot, rather
        // than being deleted, so the "no longer available" indicator has the last-known details.
        foreach (var step in referencingSteps)
        {
            step.TaskNameSnapshot = task.Name;
            step.TaskTypeSnapshot = task.TaskType;
            step.TaskId = null;
        }

        if (task.AttachedFileBlobPath is not null)
        {
            await _blobStorageService.DeleteAsync(_dbContext.CollectionId, task.AttachedFileBlobPath, cancellationToken);
        }

        _dbContext.Tasks.Remove(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new NoContentResult();
    }

    [Function("GetTaskFile")]
    public async Task<IActionResult> GetFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks/{id:guid}/file")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, _dbContext) is { } scopeError) return scopeError;
        var task = await _dbContext.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (task?.AttachedFileBlobPath is null)
        {
            return new NotFoundResult();
        }

        if (_blobStorageService is DisabledBlobStorageService)
            return new ObjectResult(new { error = "attachments_unavailable" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        var stream = await _blobStorageService.OpenReadAsync(_dbContext.CollectionId, task.AttachedFileBlobPath, cancellationToken);
        if (stream is null)
        {
            return new NotFoundResult();
        }

        var contentType = _fileValidationService.GetContentType(task.AttachedFileName ?? "file");
        return new FileStreamResult(stream, contentType)
        {
            FileDownloadName = task.AttachedFileName ?? "file",
        };
    }

    /// <summary>
    /// Shared by GetById (T019) and the create/update handlers added in T028/T030 so every route
    /// returning a full TaskItem record uses the exact same shape.
    /// </summary>
    internal static TaskDetailDto ToDetailDto(TaskItem task, List<Guid> referencedByWorkflowIds, bool publicOnly = false)
    {
        TaskFileDto? attachedFile = task.AttachedFileBlobPath is null
            ? null
            : new TaskFileDto(
                task.AttachedFileName ?? "file",
                task.AttachedFileSizeBytes ?? 0,
                $"/api/tasks/{task.Id}/file?collectionId={task.CollectionId}");

        return new TaskDetailDto(
            task.Id,
            task.Name,
            task.Description,
            task.TaskType,
            task.TargetIds,
            task.Tags,
            task.ReferenceLink,
            task.InlineContent,
            task.DataSource,
            task.DataOutput,
            attachedFile,
            new UserRefDto(task.CreatedByUserId, task.CreatedByUser?.DisplayName ?? "Unknown user"),
            task.CreatedAt,
            new UserRefDto(task.LastEditedByUserId, task.LastEditedByUser?.DisplayName ?? "Unknown user"),
            task.LastEditedAt,
            referencedByWorkflowIds,
            publicOnly && task.Method is null ? null : task.MethodId,
            MethodAssociation.ToDto(task.Method), task.Format, task.Setup, task.Prerequisites);
    }

    private static (int Page, int PageSize) ParsePaging(HttpRequest req)
    {
        var page = int.TryParse(req.Query["page"], out var p) && p > 0 ? p : DefaultPage;
        var pageSize = int.TryParse(req.Query["pageSize"], out var ps) && ps > 0 ? Math.Min(ps, 100) : DefaultPageSize;
        return (page, pageSize);
    }

    private async Task<List<Guid>> GetReferencedByWorkflowIdsAsync(Guid taskId, CancellationToken cancellationToken)
    {
        return await _dbContext.WorkflowSteps
            .AsNoTracking()
            .Where(s => s.TaskId == taskId)
            .Select(s => s.WorkflowId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Deserializes the `metadata` multipart part, used by both Create (T030) and Update (T031).
    /// </summary>
    private static bool TryParseMetadata(IFormCollection form, out TaskWriteDto metadata, out string? error)
    {
        var raw = form["metadata"].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            metadata = new TaskWriteDto(null, null, null, null, null, null, null, null, null, null);
            error = "The 'metadata' part is required.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TaskWriteDto>(raw, MetadataJsonOptions);
            if (parsed is null)
            {
                metadata = new TaskWriteDto(null, null, null, null, null, null, null, null, null, null);
                error = "The 'metadata' part could not be parsed.";
                return false;
            }

            metadata = parsed;
            error = null;
            return true;
        }
        catch (JsonException)
        {
            metadata = new TaskWriteDto(null, null, null, null, null, null, null, null, null, null);
            error = "The 'metadata' part is not valid JSON.";
            return false;
        }
    }

    /// <summary>
    /// Validates the required fields (FR-004) shared by Create and Update: name, description,
    /// task type, tool surface, and the "at least one content source" rule.
    /// </summary>
    public static string? ValidateFields(TaskWriteDto metadata, bool hasFile)
    {
        if (string.IsNullOrWhiteSpace(metadata.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(metadata.Description))
        {
            return "Description is required.";
        }

        var catalogError = CatalogRegistry.Default.ValidateTask(metadata.TaskType, metadata.TargetIds, metadata.Format);
        if (catalogError is not null) return catalogError;
        if (metadata.Name.Length > 200 || metadata.Description.Length > 2000 ||
            metadata.Setup?.Length > 10000 || metadata.Prerequisites?.Length > 10000)
            return "Task metadata exceeds its maximum length.";

        var hasReferenceLink = !string.IsNullOrWhiteSpace(metadata.ReferenceLink);
        var hasInlineContent = !string.IsNullOrWhiteSpace(metadata.InlineContent);

        if (hasReferenceLink && !IsSafeHttpUrl(metadata.ReferenceLink!))
        {
            return "Reference link must be a valid http:// or https:// URL.";
        }

        if (!hasReferenceLink && !hasInlineContent && !hasFile)
        {
            return "At least one of a reference link, inline content, or an uploaded file is required.";
        }

        return null;
    }

    /// <summary>
    /// Rejects any reference link that isn't a well-formed absolute http(s) URL (issue #67 —
    /// adversarial review found `javascript:alert(1)` was accepted and stored verbatim, then
    /// rendered as a clickable link on the task detail page). This is the authoritative,
    /// server-side check; the public API is the actual trust boundary here since writes require no
    /// sign-in.
    /// </summary>
    private static bool IsSafeHttpUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https";
    }

    public static List<string> NormalizeTags(List<string>? tags)
    {
        if (tags is null)
        {
            return new List<string>();
        }

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
