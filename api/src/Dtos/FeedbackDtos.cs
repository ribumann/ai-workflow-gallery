namespace WorkflowGallery.Api.Dtos;

/// <summary>Request body for `POST /api/feedback`.</summary>
public record FeedbackRequestDto(string Message, string? PageContext);

/// <summary>`202 Accepted` response body for `POST /api/feedback`.</summary>
public record FeedbackResponseDto(string OperationId);
