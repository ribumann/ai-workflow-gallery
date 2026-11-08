using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Functions;

public class Methods(AppDbContext dbContext, ICurrentUserService currentUserService)
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Function("ListMethods")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "methods")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext) is { } scopeError) return scopeError;
        if (!TryPage(req, "page", 1, int.MaxValue, out var page) ||
            !TryPage(req, "pageSize", 25, 100, out var pageSize) ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest("Use a positive page and a pageSize between 1 and 100 within the supported paging range.");

        var search = req.Query["q"].ToString().Trim();
        if (search.Length > 200) return BadRequest("Search must be 200 characters or fewer.");
        var query = dbContext.AiMethods.AsNoTracking().AsQueryable();
        if (search.Length > 0)
        {
            var needle = search.ToLowerInvariant();
            query = query.Where(m => m.Title.ToLower().Contains(needle) || m.Summary.ToLower().Contains(needle));
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(m => m.Title).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new MethodSummaryDto(m.Id, m.Title, m.Summary,
                m.CreatedByUser == null ? "Unknown user" : m.CreatedByUser.DisplayName, m.LastEditedAt))
            .ToListAsync(cancellationToken);
        return new OkObjectResult(new PagedResultDto<MethodSummaryDto>(items, totalCount, page, pageSize));
    }

    [Function("GetMethod")]
    public virtual async Task<IActionResult> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "methods/{id:guid}")] HttpRequest req,
        Guid id, CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext) is { } scopeError) return scopeError;
        var method = await dbContext.AiMethods.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new
            {
                m.Id,
                m.Title,
                m.Summary,
                m.Approach,
                m.CreatedByUserId,
                m.CreatedAt,
                m.LastEditedByUserId,
                m.LastEditedAt,
                m.Version,
                CreatedByDisplayName = m.CreatedByUser == null ? "Unknown user" : m.CreatedByUser.DisplayName,
                LastEditedByDisplayName = m.LastEditedByUser == null ? "Unknown user" : m.LastEditedByUser.DisplayName,
                CreatedByTenantId = m.CreatedByUser == null ? null : m.CreatedByUser.EntraTenantId,
                CreatedByObjectId = m.CreatedByUser == null ? null : m.CreatedByUser.EntraObjectId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        // Reads must not upsert a user or refresh their display name.
        if (method is null) return new NotFoundResult();
        var identity = GalleryIdentity.Key(req);
        var canDelete = GalleryIdentity.IsAdmin(req) || identity is { } key &&
            string.Equals(key.TenantId, method.CreatedByTenantId, StringComparison.Ordinal) &&
            string.Equals(key.ObjectId, method.CreatedByObjectId, StringComparison.Ordinal);
        return new OkObjectResult(new MethodDetailDto(
            method.Id,
            method.Title,
            method.Summary,
            method.Approach,
            new UserRefDto(method.CreatedByUserId, method.CreatedByDisplayName),
            method.CreatedAt,
            new UserRefDto(method.LastEditedByUserId, method.LastEditedByDisplayName),
            method.LastEditedAt,
            method.Version,
            canDelete));
    }

    [Function("CreateMethod")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "methods")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var (input, error) = await ReadInput(req, false, cancellationToken);
        if (error is not null) return BadRequest(error);
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();
        var now = DateTime.UtcNow;
        var method = new AiMethod
        {
            Id = Guid.NewGuid(), Title = input!.Title!.Trim(), Summary = input.Summary!.Trim(),
            Approach = input.Approach!.Trim(), Version = Guid.NewGuid(),
            CreatedByUserId = user.Id, CreatedByUser = user, CreatedAt = now,
            LastEditedByUserId = user.Id, LastEditedByUser = user, LastEditedAt = now,
        };
        dbContext.AiMethods.Add(method);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CreatedResult($"/api/methods/{method.Id}?collectionId={method.CollectionId}", ToDto(method, req, user));
    }

    [Function("UpdateMethod")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "methods/{id:guid}")] HttpRequest req,
        Guid id, CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var (input, error) = await ReadInput(req, true, cancellationToken);
        if (error is not null) return BadRequest(error);
        var method = await Load(id).SingleOrDefaultAsync(cancellationToken);
        if (method is null) return new NotFoundResult();
        if (input!.ExpectedVersion != method.Version) return Conflict();
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();

        method.Title = input.Title!.Trim();
        method.Summary = input.Summary!.Trim();
        method.Approach = input.Approach!.Trim();
        method.LastEditedByUserId = user.Id;
        method.LastEditedByUser = user;
        method.LastEditedAt = DateTime.UtcNow;
        method.Version = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await ConflictOrMissing(id, cancellationToken);
        }
        return new OkObjectResult(ToDto(method, req, user));
    }

    [Function("DeleteMethod")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "methods/{id:guid}")] HttpRequest req,
        Guid id, CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();
        var method = await Load(id).SingleOrDefaultAsync(cancellationToken);
        if (method is null) return new NotFoundResult();
        if (!CanDelete(method, req, user)) return new StatusCodeResult(StatusCodes.Status403Forbidden);
        var references = await References(id, cancellationToken);
        if (references.ReferencedByTasks.Count > 0 || references.ReferencedByWorkflows.Count > 0)
            return new ConflictObjectResult(references);

        dbContext.AiMethods.Remove(method);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await ConflictOrMissing(id, cancellationToken);
        }
        catch (DbUpdateException ex) when (MethodAssociation.IsReferenceViolation(ex))
        {
            dbContext.ChangeTracker.Clear();
            return new ConflictObjectResult(await References(id, cancellationToken));
        }
        return new NoContentResult();
    }

    private IQueryable<AiMethod> Load(Guid id) => dbContext.AiMethods
        .Include(m => m.CreatedByUser).Include(m => m.LastEditedByUser).Where(m => m.Id == id);

    private async Task<MethodDeleteConflictDto> References(Guid id, CancellationToken cancellationToken) => new(
        "This AI method is in use. Remove its associations from the listed tasks and workflows before deleting it.",
        await dbContext.Tasks.AsNoTracking().Where(t => t.MethodId == id).OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new MethodUsageDto(t.Id, t.Name)).ToListAsync(cancellationToken),
        await dbContext.Workflows.AsNoTracking().Where(w => w.MethodId == id).OrderBy(w => w.Name).ThenBy(w => w.Id)
            .Select(w => new MethodUsageDto(w.Id, w.Name)).ToListAsync(cancellationToken));

    private async Task<IActionResult> ConflictOrMissing(Guid id, CancellationToken cancellationToken) =>
        await dbContext.AiMethods.AsNoTracking().AnyAsync(m => m.Id == id, cancellationToken)
            ? Conflict() : new NotFoundResult();

    private static MethodDetailDto ToDto(AiMethod method, HttpRequest req, User? user = null) => new(
        method.Id, method.Title, method.Summary, method.Approach,
        new(method.CreatedByUserId, method.CreatedByUser?.DisplayName ?? "Unknown user"), method.CreatedAt,
        new(method.LastEditedByUserId, method.LastEditedByUser?.DisplayName ?? "Unknown user"), method.LastEditedAt,
        method.Version, CanDelete(method, req, user));

    private static bool CanDelete(AiMethod method, HttpRequest req, User? user)
    {
        if (GalleryIdentity.IsAdmin(req)) return true;
        if (user is not null) return method.CreatedByUserId == user.Id;
        var key = GalleryIdentity.Key(req);
        return key is not null &&
            string.Equals(key.Value.TenantId, method.CreatedByUser?.EntraTenantId, StringComparison.Ordinal) &&
            string.Equals(key.Value.ObjectId, method.CreatedByUser?.EntraObjectId, StringComparison.Ordinal);
    }

    private static BadRequestObjectResult BadRequest(string message) => new(new ErrorResponseDto(message));
    private static ConflictObjectResult Conflict() =>
        new(new ErrorResponseDto("This AI method changed. Reload its latest version before retrying."));

    private static bool TryPage(HttpRequest req, string key, int fallback, int max, out int value)
    {
        value = fallback;
        return !req.Query.ContainsKey(key) ||
            (int.TryParse(req.Query[key], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= max);
    }

    private static async Task<(MethodWriteDto? Input, string? Error)> ReadInput(
        HttpRequest req, bool requireVersion, CancellationToken cancellationToken)
    {
        MethodWriteDto? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<MethodWriteDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return (null, "The request body must be valid JSON.");
        }
        if (input is null) return (null, "A request body is required.");
        return (input, ValidateFields(input, requireVersion));
    }

    public static string? ValidateFields(MethodWriteDto input, bool requireVersion = false)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 200)
            return "Title is required and must be 200 characters or fewer.";
        if (string.IsNullOrWhiteSpace(input.Summary) || input.Summary.Trim().Length > 2000)
            return "Summary is required and must be 2000 characters or fewer.";
        if (string.IsNullOrWhiteSpace(input.Approach) || input.Approach.Trim().Length > 100000)
            return "Approach is required and must be 100000 characters or fewer.";
        if (requireVersion && (input.ExpectedVersion is null || input.ExpectedVersion == Guid.Empty))
            return "ExpectedVersion is required when editing an AI method.";
        return null;
    }
}
