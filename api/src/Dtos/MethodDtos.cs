namespace WorkflowGallery.Api.Dtos;

public record MethodSummaryDto(
    Guid Id, string Title, string Summary, string CreatedByDisplayName, DateTime LastEditedAt);

public record MethodDetailDto(
    Guid Id, string Title, string Summary, string Approach,
    UserRefDto CreatedBy, DateTime CreatedAt, UserRefDto LastEditedBy, DateTime LastEditedAt,
    Guid Version, bool CanDelete);

public record MethodWriteDto(string? Title, string? Summary, string? Approach, Guid? ExpectedVersion = null);

public record MethodRefDto(Guid Id, string Title, string Summary);

public record MethodUsageDto(Guid Id, string Name);

public record MethodDeleteConflictDto(
    string Message, List<MethodUsageDto> ReferencedByTasks, List<MethodUsageDto> ReferencedByWorkflows);
