using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Services;

public sealed record CatalogLabel(string Id, string Label);
public sealed record CatalogProfile(string Kind, List<string> Formats, string SourceUrl, string ReviewedOn, string Note);
public sealed record CatalogTarget(string Id, string Label, List<string> Aliases, string Provider, string Product,
    string Host, string Role, string Prerequisites, List<CatalogProfile> Profiles);
public sealed record CatalogDefinition(int Version, List<CatalogLabel> Kinds, List<CatalogLabel> Formats, List<CatalogTarget> Targets);

/// <summary>The embedded, shared compatibility registry is the sole authority for artifact support.</summary>
public sealed class CatalogRegistry
{
    private static readonly Lazy<CatalogRegistry> Embedded = new(() =>
    {
        using var stream = typeof(CatalogRegistry).Assembly.GetManifestResourceStream("CatalogRegistry.json")
            ?? throw new InvalidOperationException("The shared catalog registry is missing.");
        return new CatalogRegistry(JsonSerializer.Deserialize<CatalogDefinition>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The shared catalog registry is empty."));
    });
    public static CatalogRegistry Default => Embedded.Value;
    public CatalogDefinition Definition { get; }
    public IReadOnlyCollection<string> KindIds => Definition.Kinds.Select(x => x.Id).ToArray();
    public IReadOnlyCollection<string> FormatIds => Definition.Formats.Select(x => x.Id).ToArray();
    public IReadOnlyCollection<string> TargetIds => Definition.Targets.Select(x => x.Id).ToArray();

    public CatalogRegistry(CatalogDefinition definition)
    {
        Definition = definition;
        if (definition.Version != 1) throw new InvalidOperationException("Unsupported catalog registry version.");
        ValidateIds(definition.Kinds.Select(x => x.Id), 32);
        ValidateIds(definition.Formats.Select(x => x.Id), 128);
        ValidateIds(definition.Targets.Select(x => x.Id), 512);
        var roles = new[] { "execution", "context", "output", "builder", "governance", "access-program" };
        foreach (var target in definition.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Label) || !roles.Contains(target.Role) ||
                target.Aliases is null || target.Profiles is null || target.Prerequisites is null ||
                string.IsNullOrWhiteSpace(target.Provider) || string.IsNullOrWhiteSpace(target.Product) ||
                string.IsNullOrWhiteSpace(target.Host))
                throw new InvalidOperationException($"Invalid target metadata: {target.Id}.");
            var profileKinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in target.Profiles)
            {
                if (!IsKind(profile.Kind) || !profileKinds.Add(profile.Kind) ||
                    profile.Formats is null || profile.Formats.Distinct(StringComparer.Ordinal).Count() != profile.Formats.Count ||
                    profile.Formats.Any(f => !IsFormat(f)) ||
                    !Uri.TryCreate(profile.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" ||
                    !DateOnly.TryParseExact(profile.ReviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _) || string.IsNullOrWhiteSpace(profile.Note))
                    throw new InvalidOperationException($"Invalid compatibility profile: {target.Id}/{profile.Kind}.");
            }
        }
        if (definition.Kinds.Concat(definition.Formats).Any(x => string.IsNullOrWhiteSpace(x.Label)))
            throw new InvalidOperationException("Registry labels are required.");
    }

    private static void ValidateIds(IEnumerable<string> source, int maximum)
    {
        var ids = source.ToList();
        if (ids.Count == 0 || ids.Count > maximum ||
            ids.Any(x => x is null || !Regex.IsMatch(x, "^[a-z][a-z0-9.-]{0,79}$")) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("Registry IDs must be bounded, lowercase, and unique.");
    }
    public bool IsKind(string? id) => Definition.Kinds.Any(x => x.Id == id);
    public bool IsFormat(string? id) => Definition.Formats.Any(x => x.Id == id);
    public bool IsTarget(string? id) => Definition.Targets.Any(x => x.Id == id);
    public bool Supports(string? kind, string? targetId, string? format) =>
        Definition.Targets.Any(t => t.Id == targetId &&
            t.Profiles.Any(p => p.Kind == kind && (format is null || p.Formats.Contains(format, StringComparer.Ordinal))));

    public string? ValidateTask(string? kind, IReadOnlyList<string>? targetIds, string? format)
    {
        if (!IsKind(kind)) return "A valid lowercase task type is required.";
        if (targetIds is null || targetIds.Count is < 1 or > 16 ||
            targetIds.Distinct(StringComparer.Ordinal).Count() != targetIds.Count)
            return "Target IDs must contain 1 to 16 unique registry IDs.";
        if (format is not null && !IsFormat(format)) return "Unknown format ID.";
        if (targetIds.Any(id => !IsTarget(id))) return "Unknown target ID.";
        if (targetIds.Any(id => !Supports(kind, id, format))) return "Task kind/format is not supported on every selected target.";
        return null;
    }

    public string? ValidateStep(TaskItem? task, string? targetId, bool isManual)
    {
        if (isManual) return targetId is null && task is null ? null : "Manual steps must have null taskId and targetId.";
        if (task is null) return "The referenced task does not exist.";
        if (targetId is null || !task.TargetIds.Contains(targetId, StringComparer.Ordinal) ||
            !Supports(task.TaskType, targetId, task.Format))
            return "Step targetId must be a supported target belonging to the referenced task.";
        return null;
    }
}
