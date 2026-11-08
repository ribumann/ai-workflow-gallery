namespace WorkflowGallery.Api.Models;

/// <summary>
/// One device's 👍/👎 vote on a task or workflow. Anonymous by design — ratings
/// key off a per-browser `DeviceId` rather than the signed-in user, so uniqueness is
/// per `(SubjectType, SubjectId, DeviceId)` instead of per user.
/// </summary>
public class Rating
{
    public string CollectionId { get; set; } = "public-techniques";

    public Guid Id { get; set; }

    public EngagementSubjectType SubjectType { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>Random id the frontend generates once per browser and persists in localStorage.</summary>
    public required string DeviceId { get; set; }

    /// <summary>+1 ("Helpful") or -1 ("Not helpful"). No other values are valid.</summary>
    public int Value { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
