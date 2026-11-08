using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Dtos;

/// <summary>Minimal user reference embedded in detail DTOs (createdBy/lastEditedBy).</summary>
public record UserRefDto(Guid Id, string DisplayName);

/// <summary>Row shape for `GET /api/workflows`.</summary>
public record WorkflowSummaryDto(
    Guid Id,
    string Name,
    string ProblemStatement,
    string DesiredOutcome,
    int StepCount,
    List<string> TargetIds,
    string CreatedByDisplayName,
    DateTime LastEditedAt);

/// <summary>One step embedded in <see cref="WorkflowDetailDto"/>.</summary>
public record WorkflowStepDto(
    int SequenceOrder,
    Guid? TaskId,
    bool TaskAvailable,
    string TaskName,
    string? TaskType,
    string? TargetId,
    string? Notes,
    bool IsManualStep,
    string? ManualStepInstructions,
    string? DataSource,
    string? DataOutput,
    List<string> TaskTargetIds);

/// <summary>Full record shape for `GET /api/workflows/{id}`.</summary>
public record WorkflowDetailDto(
    Guid Id,
    string Name,
    string ProblemStatement,
    string DesiredOutcome,
    List<WorkflowStepDto> Steps,
    UserRefDto CreatedBy,
    DateTime CreatedAt,
    UserRefDto LastEditedBy,
    DateTime LastEditedAt,
    Guid? MethodId = null,
    MethodRefDto? Method = null);

/// <summary>One step in a `POST`/`PUT /api/workflows` request body.</summary>
public record WorkflowStepInputDto(
    Guid? TaskId,
    string? TargetId,
    string? Notes,
    bool IsManualStep,
    string? ManualStepTitle,
    string? ManualStepInstructions,
    string? DataSource,
    string? DataOutput);

/// <summary>Request body for `POST /api/workflows`.</summary>
public record CreateWorkflowRequestDto(
    string? Name,
    string? ProblemStatement,
    string? DesiredOutcome,
    List<WorkflowStepInputDto>? Steps,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement MethodId = default);

/// <summary>
/// Request body for `PUT /api/workflows/{id}` (T039). Same shape as create, plus the optional
/// optimistic-concurrency guard (FR-007).
/// </summary>
public record UpdateWorkflowRequestDto(
    string? Name,
    string? ProblemStatement,
    string? DesiredOutcome,
    List<WorkflowStepInputDto>? Steps,
    DateTime? ExpectedLastEditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonElement MethodId = default);

/// <summary>Error body for `400`/other failed write responses.</summary>
/// (See <c>TaskDtos.cs</c> for <c>ErrorResponseDto</c> — shared across both DTO files.)
