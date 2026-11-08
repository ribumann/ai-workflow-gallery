namespace WorkflowGallery.Api.Models;

/// <summary>
/// A delegated gallery identity. Legacy rows without a directory remain unlinked.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    public string? EntraTenantId { get; set; }

    /// <summary>Unique together with the validated Entra directory ID.</summary>
    public string? EntraObjectId { get; set; }

    public required string DisplayName { get; set; }

    public bool IsSandboxActor { get; set; }

    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();

    public ICollection<TaskItem> LastEditedTasks { get; set; } = new List<TaskItem>();

    public ICollection<Workflow> CreatedWorkflows { get; set; } = new List<Workflow>();

    public ICollection<Workflow> LastEditedWorkflows { get; set; } = new List<Workflow>();
}
