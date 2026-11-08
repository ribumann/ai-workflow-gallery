using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Services.Catalog;

public sealed class CatalogImportReceipt
{
    public required string CollectionId { get; set; }
    public required string DatasetId { get; set; }
    public required string ContentHash { get; set; }
    public string StateHash { get; set; } = "";
}
public sealed record CatalogImportResult(string DatasetId, int Methods, int Tasks, int Workflows, bool DryRun, bool AlreadyImported);

/// <summary>Local tooling entry point. Never creates identities, resets data, or modifies a populated collection.</summary>
public static class CatalogImportService
{
    public const int MaximumDatasetBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CatalogImportResult> ImportAsync(AppDbContext db, Stream json, User actor, bool dryRun,
        CancellationToken ct = default)
    {
        using var document = await ReadBoundedAsync(json, ct);
        return await ImportAsync(db, document.RootElement, actor, dryRun, ct);
    }

    public static async Task<CatalogImportResult> ImportAsync(AppDbContext db, Stream json, User actor, bool dryRun,
        bool publishReviewed, CancellationToken ct = default)
    {
        using var document = await ReadBoundedAsync(json, ct);
        return await ImportAsync(db, document.RootElement, actor, dryRun, ct, publishReviewed);
    }

    public static async Task<CatalogImportResult> ImportAsync(AppDbContext db, JsonElement envelope, User principal, bool dryRun = false,
        CancellationToken cancellationToken = default, bool publishReviewed = false)
    {
        if (!db.Database.IsRelational()) throw new InvalidOperationException("Catalog import requires a transactional relational database.");
        if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > MaximumDatasetBytes)
            throw new InvalidOperationException("Dataset exceeds the 5 MiB import limit.");
        if (envelope.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported dataset schema.");
        var collection = envelope.GetProperty("collectionId").GetString();
        if (!CollectionScope.IsValid(collection)) throw new InvalidOperationException("A valid explicit collectionId is required.");
        ValidateCollectionReferences(envelope, collection!);
        foreach (var (name, maximum) in new[] { ("tasks", 5000), ("workflows", 2000), ("methods", 1000) })
        {
            if (!envelope.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum)
                throw new InvalidOperationException($"Dataset '{name}' must be an array with at most {maximum} records.");
            foreach (var item in array.EnumerateArray())
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("collectionId", out var recordCollection) ||
                    recordCollection.ValueKind != JsonValueKind.String || recordCollection.GetString() != collection)
                    throw new InvalidOperationException("Every imported catalog record requires the envelope collectionId.");
        }
        db.CollectionId = collection!;
        var datasetId = envelope.GetProperty("datasetId").GetString();
        if (string.IsNullOrWhiteSpace(datasetId) || datasetId.Length > 200)
            throw new InvalidOperationException("A bounded datasetId is required.");
        if (principal.Id == Guid.Empty || !Guid.TryParse(principal.EntraTenantId, out _) ||
            !Guid.TryParse(principal.EntraObjectId, out _) ||
            !await db.Users.AnyAsync(u => u.Id == principal.Id && u.EntraTenantId == principal.EntraTenantId &&
                u.EntraObjectId == principal.EntraObjectId, cancellationToken))
            throw new InvalidOperationException("Import requires an existing real directory user principal.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{publishReviewed}\n{envelope.GetRawText()}")));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var receipt = await db.Set<CatalogImportReceipt>().SingleOrDefaultAsync(
                r => r.CollectionId == collection && r.DatasetId == datasetId, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.ContentHash != hash) throw new InvalidOperationException("This dataset ID was already imported with different content.");
                if (receipt.StateHash != await StateHashAsync(db, cancellationToken))
                    throw new InvalidOperationException("This imported collection has been edited; it cannot be reimported.");
                await transaction.RollbackAsync(cancellationToken);
                return new(datasetId, 0, 0, 0, dryRun, true);
            }
            if (await db.Tasks.AnyAsync(cancellationToken) || await db.Workflows.AnyAsync(cancellationToken) ||
                await db.AiMethods.AnyAsync(cancellationToken) || await db.ReportLinks.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Import requires an empty catalog collection.");
            var now = DateTime.UtcNow;
            var methods = new Dictionary<Guid, AiMethod>();
            foreach (var item in envelope.GetProperty("methods").EnumerateArray())
            {
                var input = item.Deserialize<MethodWriteDto>(JsonOptions) ?? throw new InvalidOperationException("Invalid method.");
                Reject(Methods.ValidateFields(input));
                var method = new AiMethod
                {
                    Id = Id(item), Title = input.Title!.Trim(), Summary = input.Summary!.Trim(), Approach = input.Approach!.Trim(),
                    Version = Guid.NewGuid(), CreatedByUserId = principal.Id, LastEditedByUserId = principal.Id,
                    CreatedAt = now, LastEditedAt = now, CollectionId = collection!, IsPublished = publishReviewed
                };
                if (!methods.TryAdd(method.Id, method)) throw new InvalidOperationException("Duplicate method ID.");
            }
            var tasks = new Dictionary<Guid, TaskItem>();
            foreach (var item in envelope.GetProperty("tasks").EnumerateArray())
            {
                var input = item.Deserialize<TaskWriteDto>(JsonOptions) ?? throw new InvalidOperationException("Invalid task.");
                Reject(Functions.Tasks.ValidateFields(input, false));
                var task = new TaskItem
                {
                    Id = Id(item), Name = input.Name!.Trim(), Description = input.Description!.Trim(),
                    TaskType = input.TaskType!, TargetIds = input.TargetIds!.ToList(), Format = input.Format,
                    Setup = Blank(input.Setup), Prerequisites = Blank(input.Prerequisites), Tags = Functions.Tasks.NormalizeTags(input.Tags),
                    ReferenceLink = Blank(input.ReferenceLink), InlineContent = Blank(input.InlineContent),
                    DataSource = Blank(input.DataSource), DataOutput = Blank(input.DataOutput), MethodId = MethodId(item, methods),
                    CreatedByUserId = principal.Id, LastEditedByUserId = principal.Id, CreatedAt = now, LastEditedAt = now,
                    CollectionId = collection!, IsPublished = publishReviewed
                };
                if (!tasks.TryAdd(task.Id, task)) throw new InvalidOperationException("Duplicate task ID.");
            }
            var workflows = new Dictionary<Guid, Workflow>();
            foreach (var item in envelope.GetProperty("workflows").EnumerateArray())
            {
                var input = item.Deserialize<CreateWorkflowRequestDto>(JsonOptions) ?? throw new InvalidOperationException("Invalid workflow.");
                Reject(Functions.Workflows.ValidateInput(input.Name, input.ProblemStatement, input.DesiredOutcome, input.Steps));
                var workflow = new Workflow
                {
                    Id = Id(item), Name = input.Name!.Trim(), ProblemStatement = input.ProblemStatement!.Trim(),
                    DesiredOutcome = input.DesiredOutcome!.Trim(), MethodId = MethodId(item, methods),
                    CreatedByUserId = principal.Id, LastEditedByUserId = principal.Id, CreatedAt = now, LastEditedAt = now,
                    CollectionId = collection!, IsPublished = publishReviewed
                };
                foreach (var step in input.Steps!)
                {
                    var task = step.TaskId is { } taskId ? tasks.GetValueOrDefault(taskId) : null;
                    Reject(CatalogRegistry.Default.ValidateStep(task, step.TargetId, step.IsManualStep));
                }
                workflow.Steps = Functions.Workflows.BuildSteps(workflow.Id, input.Steps!);
                if (!workflows.TryAdd(workflow.Id, workflow)) throw new InvalidOperationException("Duplicate workflow ID.");
            }
            db.AiMethods.AddRange(methods.Values);
            db.Tasks.AddRange(tasks.Values);
            db.Workflows.AddRange(workflows.Values);
            db.Set<CatalogImportReceipt>().Add(new() { CollectionId = collection!, DatasetId = datasetId, ContentHash = hash });
            await db.SaveChangesAsync(cancellationToken);
            var importedReceipt = await db.Set<CatalogImportReceipt>().SingleAsync(
                r => r.CollectionId == collection && r.DatasetId == datasetId, cancellationToken);
            importedReceipt.StateHash = await StateHashAsync(db, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (dryRun) await transaction.RollbackAsync(cancellationToken);
            else await transaction.CommitAsync(cancellationToken);
            return new(datasetId, methods.Count, tasks.Count, workflows.Count, dryRun, false);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static void Reject(string? error) { if (error is not null) throw new InvalidOperationException(error); }
    private static async Task<string> StateHashAsync(AppDbContext db, CancellationToken ct)
    {
        var tasks = await db.Tasks.AsNoTracking().OrderBy(t => t.Id).Select(t => new
        {
            t.Id, t.CollectionId, t.IsPublished, t.Name, t.Description, t.TaskType, t.TargetIds, t.Format, t.Setup,
            t.Prerequisites, t.Tags, t.ReferenceLink, t.InlineContent, t.DataSource, t.DataOutput,
            t.AttachedFileBlobPath, t.MethodId, t.CreatedByUserId, t.LastEditedByUserId, t.CreatedAt, t.LastEditedAt
        }).ToListAsync(ct);
        var methods = await db.AiMethods.AsNoTracking().OrderBy(m => m.Id).Select(m => new
        {
            m.Id, m.CollectionId, m.IsPublished, m.Title, m.Summary, m.Approach, m.Version, m.CreatedByUserId,
            m.LastEditedByUserId, m.CreatedAt, m.LastEditedAt
        }).ToListAsync(ct);
        var workflows = await db.Workflows.AsNoTracking().OrderBy(w => w.Id).Select(w => new
        {
            w.Id, w.CollectionId, w.IsPublished, w.Name, w.ProblemStatement, w.DesiredOutcome,
            w.MethodId, w.CreatedByUserId, w.LastEditedByUserId, w.CreatedAt, w.LastEditedAt
        }).ToListAsync(ct);
        var steps = await db.WorkflowSteps.AsNoTracking().OrderBy(s => s.WorkflowId).ThenBy(s => s.SequenceOrder).Select(s => new
        {
            s.Id, s.WorkflowId, s.SequenceOrder, s.TaskId, s.TargetId, s.TaskNameSnapshot, s.TaskTypeSnapshot,
            s.IsManualStep, s.ManualStepTitle, s.ManualStepInstructions, s.Notes, s.DataSource, s.DataOutput
        }).ToListAsync(ct);
        var reports = await db.ReportLinks.AsNoTracking().OrderBy(r => r.Id).Select(r => new
        {
            r.Id, r.CollectionId, r.IsPublished, r.Name, r.Description, r.Url, r.Kind, r.Version
        }).ToListAsync(ct);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { tasks, methods, workflows, steps, reports })));
    }
    private static async Task<JsonDocument> ReadBoundedAsync(Stream json, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await json.ReadAsync(chunk.AsMemory(), ct)) != 0)
        {
            if (buffer.Length + read > MaximumDatasetBytes)
                throw new InvalidOperationException("Dataset exceeds the 5 MiB import limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 64 }, ct);
    }

    private static void ValidateCollectionReferences(JsonElement element, string collection)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("collectionId", StringComparison.OrdinalIgnoreCase) &&
                    (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() != collection))
                    throw new InvalidOperationException("Imported records must belong to the envelope collectionId.");
                ValidateCollectionReferences(property.Value, collection);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) ValidateCollectionReferences(value, collection);
    }
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static Guid Id(JsonElement item) => item.TryGetProperty("id", out var id) && id.TryGetGuid(out var result) && result != Guid.Empty
        ? result : throw new InvalidOperationException("Every imported record requires a nonempty GUID id.");
    private static Guid? MethodId(JsonElement item, Dictionary<Guid, AiMethod> methods)
    {
        if (!item.TryGetProperty("methodId", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetGuid(out var id) || !methods.ContainsKey(id)) throw new InvalidOperationException("Method association is unavailable in this collection.");
        return id;
    }
}
