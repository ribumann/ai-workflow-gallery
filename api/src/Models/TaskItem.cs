namespace WorkflowGallery.Api.Models;

/// <summary>
/// A reusable building block (agent, skill, plugin, prompt, or MCP server/tool). See
/// Named <c>TaskItem</c> rather than <c>Task</c> to avoid
/// colliding with <see cref="System.Threading.Tasks.Task"/>, which is used throughout this
/// codebase for async method signatures (issue #76 rename).
/// </summary>
public class TaskItem
{
    public string CollectionId { get; set; } = "public-techniques";
    public bool IsPublished { get; set; }
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public string TaskType { get; set; } = "";
    public List<string> TargetIds { get; set; } = new();
    public string? Format { get; set; }
    public string? Setup { get; set; }
    public string? Prerequisites { get; set; }

    /// <summary>At least one of ReferenceLink / InlineContent / AttachedFileBlobPath is required (FR-004).</summary>
    public string? ReferenceLink { get; set; }

    public string? InlineContent { get; set; }

    /// <summary>Optional free-text description of what this task expects as input (issue #67).</summary>
    public string? DataSource { get; set; }

    /// <summary>Optional free-text description of what this task produces (issue #67).</summary>
    public string? DataOutput { get; set; }

    /// <summary>Path/key into the Blob Storage container; null if no file attached.</summary>
    public string? AttachedFileBlobPath { get; set; }

    /// <summary>Original filename, shown to viewers on download.</summary>
    public string? AttachedFileName { get; set; }

    public long? AttachedFileSizeBytes { get; set; }

    /// <summary>Normalized (lowercase) tags supporting keyword search (FR-003).</summary>
    public List<string> Tags { get; set; } = new();

    public Guid CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid LastEditedByUserId { get; set; }

    public User? LastEditedByUser { get; set; }

    public DateTime LastEditedAt { get; set; }

    public Guid? MethodId { get; set; }

    public AiMethod? Method { get; set; }

    public ICollection<WorkflowStep> WorkflowSteps { get; set; } = new List<WorkflowStep>();
}
