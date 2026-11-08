namespace WorkflowGallery.Api.Models;

/// <summary>
/// One position within a Workflow's ordered sequence.
/// </summary>
public class WorkflowStep
{
    public string CollectionId { get; set; } = "public-techniques";

    public Guid Id { get; set; }

    public Guid WorkflowId { get; set; }

    public Workflow? Workflow { get; set; }

    /// <summary>Unique within a Workflow; defines display/execution order (FR-008).</summary>
    public int SequenceOrder { get; set; }

    /// <summary>
    /// Null means the referenced Task was deleted; the row itself is never deleted (FR-010).
    /// ON DELETE SET NULL is configured in AppDbContext.
    /// </summary>
    public Guid? TaskId { get; set; }

    public TaskItem? Task { get; set; }

    /// <summary>
    /// Populated only when TaskId is set to null by the Task's deletion (captures its
    /// last-known name); null while the task still exists.
    /// </summary>
    public string? TaskNameSnapshot { get; set; }

    public string? TaskTypeSnapshot { get; set; }

    /// <summary>
    /// Actual selected task target, retained when the task is deleted. Null for manual steps.
    /// </summary>
    public string? TargetId { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// True when this step is a manual (non-task) action a human performs as part of the
    /// workflow — e.g. "review the output before sending" (issue #67). When true, TaskId is
    /// always null and ManualStepTitle is required.
    /// </summary>
    public bool IsManualStep { get; set; }

    /// <summary>Short label shown in place of a task name for a manual step. Required when
    /// <see cref="IsManualStep"/> is true; null for task-backed steps.</summary>
    public string? ManualStepTitle { get; set; }

    /// <summary>Optional longer guidance for a manual step (what the human should do).</summary>
    public string? ManualStepInstructions { get; set; }

    /// <summary>Optional free-text description of what this step expects as input (issue #67).</summary>
    public string? DataSource { get; set; }

    /// <summary>Optional free-text description of what this step produces (issue #67).</summary>
    public string? DataOutput { get; set; }
}
