using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Dtos;

/// <summary>Row shape for `GET /api/tasks`.</summary>
public record TaskSummaryDto(
    Guid Id,
    string Name,
    string Description,
    string TaskType,
    List<string> TargetIds,
    List<string> Tags,
    bool HasReferenceLink,
    bool HasInlineContent,
    bool HasAttachedFile,
    string CreatedByDisplayName,
    DateTime LastEditedAt,
    string? Format = null,
    string? Setup = null,
    string? Prerequisites = null);

/// <summary>Attached-file summary embedded in <see cref="TaskDetailDto"/>.</summary>
public record TaskFileDto(string FileName, long SizeBytes, string DownloadUrl);

/// <summary>Full record shape for `GET /api/tasks/{id}`.</summary>
public record TaskDetailDto(
    Guid Id,
    string Name,
    string Description,
    string TaskType,
    List<string> TargetIds,
    List<string> Tags,
    string? ReferenceLink,
    string? InlineContent,
    string? DataSource,
    string? DataOutput,
    TaskFileDto? AttachedFile,
    UserRefDto CreatedBy,
    DateTime CreatedAt,
    UserRefDto LastEditedBy,
    DateTime LastEditedAt,
    List<Guid> ReferencedByWorkflowIds,
    Guid? MethodId = null,
    MethodRefDto? Method = null,
    string? Format = null,
    string? Setup = null,
    string? Prerequisites = null);

/// <summary>Paged list envelope shared by tasks and workflows list endpoints.</summary>
public record PagedResultDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>
/// Multipart metadata uses exact registry kind, target, and format IDs; no coercion or legacy aliases.
/// </summary>
public record TaskWriteDto(
    string? Name,
    string? Description,
    string? TaskType,
    List<string>? TargetIds,
    List<string>? Tags,
    string? ReferenceLink,
    string? InlineContent,
    string? DataSource,
    string? DataOutput,
    DateTime? ExpectedLastEditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement MethodId = default,
    string? Format = null,
    string? Setup = null,
    string? Prerequisites = null);

/// <summary>Minimal Workflow reference used by the delete-guard conflict body (FR-009).</summary>
public record WorkflowRefDto(Guid Id, string Name);

/// <summary>Body of the `409 Conflict` returned by `DELETE /api/tasks/{id}` when the task
/// is still referenced by one or more workflows and `?confirm=true` was not supplied.</summary>
public record DeleteConflictDto(List<WorkflowRefDto> ReferencedByWorkflows);

/// <summary>Generic error body for `400`/`401` responses from the task write endpoints.</summary>
public record ErrorResponseDto(string Message);
