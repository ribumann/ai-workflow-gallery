namespace WorkflowGallery.Api.Models;

/// <summary>
/// The kind of catalog entity a <see cref="Rating"/> or <see cref="Comment"/> is attached to.
/// </summary>
/// <remarks>
/// Persisted as <c>int</c>, so values are append-only: never reorder or reuse a number or existing
/// ratings and comments would silently re-point at a different entity kind.
/// </remarks>
public enum EngagementSubjectType
{
    Task,
    Workflow,
    Method,
}
