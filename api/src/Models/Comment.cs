namespace WorkflowGallery.Api.Models;

/// <summary>
/// A short free-text comment on a task or workflow. No edit/delete/moderation in
/// this slice.
/// </summary>
public class Comment
{
    public string CollectionId { get; set; } = "public-techniques";

    public Guid Id { get; set; }

    public EngagementSubjectType SubjectType { get; set; }

    public Guid SubjectId { get; set; }

    public required string Body { get; set; }

    public Guid CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }
}
