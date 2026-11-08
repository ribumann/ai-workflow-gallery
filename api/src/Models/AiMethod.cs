namespace WorkflowGallery.Api.Models;

public class AiMethod
{
    public string CollectionId { get; set; } = "public-techniques";
    public bool IsPublished { get; set; }
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Approach { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid LastEditedByUserId { get; set; }
    public User? LastEditedByUser { get; set; }
    public DateTime LastEditedAt { get; set; }
    public Guid Version { get; set; }
}
