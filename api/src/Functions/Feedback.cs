using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Functions;

/// <summary>
/// `/api/feedback` — sends free-text feedback to the maintainer mailbox. Feedback is never
/// persisted in this app's own database.
/// </summary>
public class Feedback
{
    private const int MaxMessageLength = 2000;
    private const int TitlePreviewLength = 80;

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICurrentUserService _currentUserService;
    private readonly IFeedbackNotificationClient _feedbackNotificationClient;
    private readonly ILogger<Feedback> _logger;
    private readonly IConfiguration _configuration;
    private readonly PublicDemoPolicy _publicDemoPolicy;

    public Feedback(ICurrentUserService currentUserService, IFeedbackNotificationClient feedbackNotificationClient, ILogger<Feedback> logger, IConfiguration configuration,
        PublicDemoPolicy? publicDemoPolicy = null)
    {
        _currentUserService = currentUserService;
        _feedbackNotificationClient = feedbackNotificationClient;
        _logger = logger;
        _configuration = configuration;
        _publicDemoPolicy = publicDemoPolicy ?? PublicDemoPolicy.FromConfiguration(configuration);
    }

    [Function("SubmitFeedback")]
    public async Task<IActionResult> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "feedback")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        if (_publicDemoPolicy.Enabled || PublicDemoPolicy.For(req).Enabled) return PublicDemoPolicy.Denied();
        if (!_configuration.GetValue<bool>("EnableFeedbackEmail"))
        {
            return new ObjectResult(new { error = "feedback_disabled", message = "Feedback delivery is not enabled. Contact the gallery owner directly." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
        }

        FeedbackRequestDto? dto;
        try
        {
            dto = await BoundedJsonBody.ReadAsync<FeedbackRequestDto>(req, 32768, RequestJsonOptions, cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new ObjectResult(new { error = "Request body must be 32768 bytes or fewer." }) { StatusCode = ex.StatusCode };
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Request body must be valid JSON." });
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Message))
        {
            return new BadRequestObjectResult(new { error = "message is required." });
        }

        var message = dto.Message.Trim();
        if (message.Length > MaxMessageLength)
        {
            return new BadRequestObjectResult(new { error = $"message must be {MaxMessageLength} characters or fewer." });
        }
        if (dto.PageContext?.Length > 2048)
        {
            return new BadRequestObjectResult(new { error = "pageContext must be 2048 characters or fewer." });
        }

        var user = await _currentUserService.GetOrCreateCurrentUserAsync(req, cancellationToken);
        if (user is null) return new UnauthorizedResult();
        var submitterName = user.DisplayName;

        var titlePreview = message.Length > TitlePreviewLength ? message[..TitlePreviewLength] : message;
        var title = $"Feedback: {titlePreview}";

        var bodyLines = new List<string>
        {
            message,
            string.Empty,
            $"Submitted by: {submitterName}",
        };

        if (!string.IsNullOrWhiteSpace(dto.PageContext))
        {
            bodyLines.Add($"Page: {dto.PageContext}");
        }

        bodyLines.Add($"Submitted at: {DateTimeOffset.UtcNow:O}");

        var body = string.Join('\n', bodyLines);

        try
        {
            var notification = await _feedbackNotificationClient.SendAsync(title, body, cancellationToken);
            return new ObjectResult(new FeedbackResponseDto(notification.OperationId))
            {
                StatusCode = StatusCodes.Status202Accepted,
            };
        }
        catch (FeedbackNotificationException ex)
        {
            _logger.LogError("Failed to confirm feedback email queuing: {ErrorType}.", ex.GetType().Name);
            return new ObjectResult(new { error = "Feedback could not be confirmed as queued. Your message may already have been accepted; contact the gallery owner before retrying." })
            {
                StatusCode = StatusCodes.Status502BadGateway,
            };
        }
    }
}
