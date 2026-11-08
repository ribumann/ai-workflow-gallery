using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Data;

public static class SeedData
{
    /// <summary>Explicit local sample, attributed only to an existing authenticated user.</summary>
    public static async Task EnsureSeededAsync(AppDbContext db, User user, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == user.Id, ct))
            throw new InvalidOperationException("Sample data requires an existing user.");
        if (await db.Tasks.AnyAsync(ct)) return;
        var target = CatalogRegistry.Default.Definition.Targets.First(t => t.Profiles.Any(p => p.Kind == "prompt"));
        var format = target.Profiles.First(p => p.Kind == "prompt").Formats.FirstOrDefault();
        var error = CatalogRegistry.Default.ValidateTask("prompt", new[] { target.Id }, format);
        if (error is not null) throw new InvalidOperationException(error);
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.NewGuid(), Name = "Summarize supplied notes", Description = "Summarize notes you explicitly provide, then review the result.",
            TaskType = "prompt", TargetIds = new() { target.Id }, Format = format,
            InlineContent = "Summarize the supplied notes. Distinguish facts, decisions, open questions, and next actions. Do not invent missing information.",
            Tags = new() { "notes", "summary" }, CreatedByUserId = user.Id, LastEditedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
