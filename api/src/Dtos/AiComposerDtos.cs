using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Dtos;

/// <summary>Request body for `POST /api/ai/compose`.</summary>
public record ComposeRequestDto(string Goal);

/// <summary>One resolved step in a `200 OK` compose response.</summary>
public record ProposedStepDto(
    int SequenceOrder,
    Guid TaskId,
    string TaskName,
    string TaskType,
    string TargetId,
    string? Notes);

/// <summary>One gap in a `200 OK` compose response.</summary>
public record GapDto(
    string Description,
    int? SequenceHint,
    string? SuggestedTaskName,
    string? SuggestedTaskDescription);

/// <summary>`200 OK` response body for `POST /api/ai/compose`.</summary>
public record ComposeResponseDto(
    string SuggestedName,
    string ProblemStatement,
    string SuggestedOutcome,
    List<ProposedStepDto> Steps,
    List<GapDto> Gaps);

/// <summary>`429 Too Many Requests` response body.</summary>
public record ComposeRateLimitedDto(int RetryAfterSeconds);
