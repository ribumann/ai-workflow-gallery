namespace WorkflowGallery.Api.Dtos;

/// <summary>
/// Aggregate rating shape returned by `GET`/`PUT /api/{tasks|workflows}/{id}/rating`.
/// </summary>
public record RatingSummaryDto(int HelpfulCount, int NotHelpfulCount, int? MyVote);

/// <summary>Body of `PUT /api/{tasks|workflows}/{id}/rating`.</summary>
public record RatingRequestDto(string? DeviceId, int? Value);

/// <summary>Row shape for `GET /api/{tasks|workflows}/{id}/comments` (issue #60).</summary>
public record CommentDto(Guid Id, string Body, string CreatedByDisplayName, DateTime CreatedAt);

/// <summary>List envelope for `GET /api/{tasks|workflows}/{id}/comments`.</summary>
public record CommentListDto(List<CommentDto> Items);

/// <summary>Body of `POST /api/{tasks|workflows}/{id}/comments`.</summary>
public record CommentRequestDto(string? Body);
