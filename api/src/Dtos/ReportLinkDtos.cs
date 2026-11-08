using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Dtos;

public record ReportLinkDto(
    Guid Id,
    string Name,
    string Description,
    string Url,
    ReportLinkKind Kind,
    string CreatedByDisplayName,
    DateTime CreatedAt,
    string LastEditedByDisplayName,
    DateTime LastEditedAt,
    Guid Version);

public record ReportLinkRequestDto(
    string? Name,
    string? Description,
    string? Url,
    ReportLinkKind? Kind,
    Guid? ExpectedVersion);
