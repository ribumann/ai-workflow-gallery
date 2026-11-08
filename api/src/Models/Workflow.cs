namespace WorkflowGallery.Api.Models;

/// <summary>
/// An ordered recipe linking two or more Tasks to solve a stated problem and produce a stated
/// outcome.
/// </summary>
public class Workflow
{
    public string CollectionId { get; set; } = "public-techniques";
    public bool IsPublished { get; set; }
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string ProblemStatement { get; set; }

    public required string DesiredOutcome { get; set; }

    public Guid CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid LastEditedByUserId { get; set; }

    public User? LastEditedByUser { get; set; }

    public DateTime LastEditedAt { get; set; }

    public Guid? MethodId { get; set; }

    public AiMethod? Method { get; set; }

    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}
