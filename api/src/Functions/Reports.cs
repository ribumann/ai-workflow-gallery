using System.Globalization;
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

/// <summary>Collaborative directory metadata only; never fetches the destination's content.</summary>
public class Reports(AppDbContext dbContext, ICurrentUserService currentUserService)
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [Function("ListReports")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext) is { } scopeError) return scopeError;
        if (!TryPage(req, "page", 1, int.MaxValue, out var page) ||
            !TryPage(req, "pageSize", 25, 100, out var pageSize) ||
            (long)(page - 1) * pageSize > int.MaxValue)
        {
            return BadRequest("Use a positive page and a pageSize between 1 and 100 within the supported paging range.");
        }

        var search = req.Query["q"].ToString().Trim();
        if (search.Length > 200)
        {
            return BadRequest("Search must be 200 characters or fewer.");
        }

        var query = dbContext.ReportLinks.AsNoTracking().AsQueryable();
        if (req.Query.ContainsKey("kind"))
        {
            var kindText = req.Query["kind"].ToString();
            if (!Enum.TryParse<ReportLinkKind>(kindText, out var kind) ||
                !Enum.IsDefined(kind) || kind.ToString() != kindText)
            {
                return BadRequest("Kind must be Report or AiApp.");
            }
            query = query.Where(r => r.Kind == kind);
        }
        if (search.Length > 0)
        {
            var needle = search.ToLowerInvariant();
            query = query.Where(r => r.Name.ToLower().Contains(needle) || r.Description.ToLower().Contains(needle));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ReportLinkDto(
                r.Id, r.Name, r.Description, r.Url, r.Kind,
                r.CreatedByUser == null ? "Unknown user" : r.CreatedByUser.DisplayName, r.CreatedAt,
                r.LastEditedByUser == null ? "Unknown user" : r.LastEditedByUser.DisplayName, r.LastEditedAt, r.Version))
            .ToListAsync(cancellationToken);
        return new OkObjectResult(new PagedResultDto<ReportLinkDto>(items, totalCount, page, pageSize));
    }

    [Function("GetReport")]
    public virtual async Task<IActionResult> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reports/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext) is { } scopeError) return scopeError;
        var link = await Load(id).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return link is null ? new NotFoundResult() : new OkObjectResult(ToDto(link));
    }

    [Function("CreateReport")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reports")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var (input, error) = await ReadInput(req, requireVersion: false, cancellationToken);
        if (error is not null) return BadRequest(error);
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();

        var now = DateTime.UtcNow;
        var link = new ReportLink
        {
            Id = Guid.NewGuid(), Name = input!.Name!.Trim(), Description = input.Description!.Trim(),
            Url = input.Url!, Kind = input.Kind!.Value, Version = Guid.NewGuid(),
            CreatedByUserId = user.Id, CreatedByUser = user, CreatedAt = now,
            LastEditedByUserId = user.Id, LastEditedByUser = user, LastEditedAt = now,
        };
        dbContext.ReportLinks.Add(link);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ObjectResult(ToDto(link)) { StatusCode = StatusCodes.Status201Created };
    }

    [Function("UpdateReport")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "reports/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var (input, error) = await ReadInput(req, requireVersion: true, cancellationToken);
        if (error is not null) return BadRequest(error);
        var link = await Load(id).SingleOrDefaultAsync(cancellationToken);
        if (link is null) return new NotFoundResult();
        if (input!.ExpectedVersion != link.Version) return Conflict();
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();

        link.Name = input.Name!.Trim();
        link.Description = input.Description!.Trim();
        link.Url = input.Url!;
        link.Kind = input.Kind!.Value;
        link.LastEditedByUserId = user.Id;
        link.LastEditedByUser = user;
        link.LastEditedAt = DateTime.UtcNow;
        link.Version = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictOrMissing(id, cancellationToken);
        }
        return new OkObjectResult(ToDto(link));
    }

    [Function("DeleteReport")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "reports/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CollectionScope.Apply(req, dbContext, write: true) is { } scopeError) return scopeError;
        var user = await currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();
        var link = await dbContext.ReportLinks.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (link is null) return new NotFoundResult();
        dbContext.ReportLinks.Remove(link);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictOrMissing(id, cancellationToken);
        }
        return new NoContentResult();
    }

    private IQueryable<ReportLink> Load(Guid id) => dbContext.ReportLinks
        .Include(r => r.CreatedByUser).Include(r => r.LastEditedByUser).Where(r => r.Id == id);

    private async Task<IActionResult> ConflictOrMissing(Guid id, CancellationToken cancellationToken) =>
        await dbContext.ReportLinks.AsNoTracking().AnyAsync(r => r.Id == id, cancellationToken)
            ? Conflict() : new NotFoundResult();

    private static ReportLinkDto ToDto(ReportLink link) => new(
        link.Id, link.Name, link.Description, link.Url, link.Kind,
        link.CreatedByUser?.DisplayName ?? "Unknown user", link.CreatedAt,
        link.LastEditedByUser?.DisplayName ?? "Unknown user", link.LastEditedAt, link.Version);

    private static BadRequestObjectResult BadRequest(string error) => new(new { error });
    private static ConflictObjectResult Conflict() => new(new { error = "This link changed. Reload its latest version before retrying." });

    private static bool TryPage(HttpRequest req, string key, int fallback, int max, out int value)
    {
        value = fallback;
        return !req.Query.ContainsKey(key) ||
            (int.TryParse(req.Query[key], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= max);
    }

    private static async Task<(ReportLinkRequestDto? Input, string? Error)> ReadInput(
        HttpRequest req, bool requireVersion, CancellationToken cancellationToken)
    {
        ReportLinkRequestDto? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<ReportLinkRequestDto>(req.Body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return (null, "The request body must be valid JSON with a Report or AiApp kind.");
        }
        if (input is null) return (null, "A request body is required.");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
            return (null, "Name is required and must be 200 characters or fewer.");
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 4000)
            return (null, "Description is required and must be 4000 characters or fewer.");
        if (input.Kind is null || !Enum.IsDefined(input.Kind.Value))
            return (null, "Kind must be Report or AiApp.");
        if (!IsSafeUrl(input.Url))
            return (null, "Provide an absolute HTTPS URL, up to 2048 characters, without whitespace, backslashes or embedded credentials.");
        if (requireVersion && (input.ExpectedVersion is null || input.ExpectedVersion == Guid.Empty))
            return (null, "ExpectedVersion is required when editing a link.");
        return (input, null);
    }

    private static bool IsSafeUrl(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 2048 &&
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
        !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.IsWellFormedOriginalString() && uri.Scheme == Uri.UriSchemeHttps &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
}
