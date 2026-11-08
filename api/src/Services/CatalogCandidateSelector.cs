using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkflowGallery.Api.Services;

public static class CatalogCandidateSelector
{
    public const int MaximumCandidates = 60;
    public const int MaximumContextCharacters = 90000;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
        { "a", "an", "the", "and", "or", "to", "for", "of", "in", "on", "with", "my", "our", "i", "we", "is", "it", "that", "this" };

    public static IReadOnlyList<CatalogTaskSnapshot> Select(string goal, IEnumerable<CatalogTaskSnapshot> catalog, bool requireRelevant = false)
    {
        var terms = Tokens(goal).Where(t => !StopWords.Contains(t)).ToHashSet(StringComparer.Ordinal);
        var ranked = catalog.Where(t => CatalogRegistry.Default.ValidateTask(t.TaskType, t.TargetIds, t.Format) is null)
            .Select(t => new { Task = t, Score = Score(t, terms) })
            .Where(t => !requireRelevant || t.Score > 0)
            .OrderByDescending(t => t.Score).ThenBy(t => t.Task.Id);
        var selected = new List<CatalogTaskSnapshot>();
        var size = 2;
        foreach (var item in ranked)
        {
            var length = JsonSerializer.Serialize(item.Task).Length + 1;
            if (size + length > MaximumContextCharacters) continue;
            selected.Add(item.Task);
            size += length;
            if (selected.Count == MaximumCandidates) break;
        }
        return selected;
    }

    public static IReadOnlyList<CatalogTaskSnapshot> SelectPublic(string goal, IEnumerable<CatalogTaskSnapshot> catalog, int maxContextBytes)
    {
        var selected = new List<CatalogTaskSnapshot>();
        foreach (var task in Select(goal, catalog, requireRelevant: true))
        {
            selected.Add(task);
            if (Encoding.UTF8.GetByteCount(AzureAiComposerService.CatalogJson(selected)) > maxContextBytes)
                selected.RemoveAt(selected.Count - 1);
        }
        return selected;
    }

    private static IEnumerable<string> Tokens(string value) =>
        Regex.Matches(value.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value);
    private static int Score(CatalogTaskSnapshot task, HashSet<string> terms)
    {
        var name = Tokens(task.Name).ToHashSet(StringComparer.Ordinal);
        var tags = task.Tags.SelectMany(Tokens).ToHashSet(StringComparer.Ordinal);
        var description = Tokens(task.Description).ToHashSet(StringComparer.Ordinal);
        var targets = task.TargetIds.SelectMany(Tokens).ToHashSet(StringComparer.Ordinal);
        return terms.Sum(t => (name.Contains(t) ? 8 : 0) + (tags.Contains(t) ? 6 : 0) +
            (description.Contains(t) ? 2 : 0) + (targets.Contains(t) ? 1 : 0));
    }
}
