using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Fake feedback notification client for exercising success and 502 paths.</summary>
public class FakeFeedbackNotificationClient : IFeedbackNotificationClient
{
    private readonly FeedbackNotification? _result;
    private readonly bool _shouldThrow;

    private FakeFeedbackNotificationClient(FeedbackNotification? result, bool shouldThrow)
    {
        _result = result;
        _shouldThrow = shouldThrow;
    }

    public string? Subject { get; private set; }

    public string? Body { get; private set; }

    public static FakeFeedbackNotificationClient Succeeding(string operationId = "operation-1") =>
        new(new FeedbackNotification(operationId), shouldThrow: false);

    public static FakeFeedbackNotificationClient Failing() => new(null, shouldThrow: true);

    public Task<FeedbackNotification> SendAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        Subject = subject;
        Body = body;

        if (_shouldThrow)
        {
            throw new FeedbackNotificationException("Simulated email delivery failure.");
        }

        return Task.FromResult(_result!);
    }
}
