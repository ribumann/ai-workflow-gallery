namespace WorkflowGallery.Api.Models;

public enum ReportLinkKind
{
    Report,
    AiApp,
}

public class ReportLink
{
    public string CollectionId { get; set; } = "public-techniques";
    public bool IsPublished { get; set; }
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Url { get; set; }
    public ReportLinkKind Kind { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid LastEditedByUserId { get; set; }
    public User? LastEditedByUser { get; set; }
    public DateTime LastEditedAt { get; set; }
    public Guid Version { get; set; }
}
