using System.Net;
using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace WorkflowGallery.Api.Services;

/// <summary>Result of a submitted feedback email notification.</summary>
public record FeedbackNotification(string OperationId);

/// <summary>Sends feedback to the maintainers without persisting it in the gallery database.</summary>
public interface IFeedbackNotificationClient
{
    Task<FeedbackNotification> SendAsync(string subject, string body, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a feedback notification could not be delivered or queued.</summary>
public class FeedbackNotificationException : Exception
{
    public FeedbackNotificationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Azure Communication Services Email implementation authenticated through the Function App's managed
/// identity via <see cref="DefaultAzureCredential"/>, avoiding the expiring GitHub PAT used by the
/// original feedback backlog integration.
/// </summary>
public class AzureCommunicationFeedbackEmailClient : IFeedbackNotificationClient
{
    internal const string EndpointSettingName = "FeedbackEmailEndpoint";
    internal const string SenderSettingName = "FeedbackEmailSenderAddress";
    internal const string RecipientSettingName = "FeedbackEmailRecipientAddress";

    private readonly IConfiguration _configuration;

    public AzureCommunicationFeedbackEmailClient(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<FeedbackNotification> SendAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_configuration.GetValue<bool>("EnableFeedbackEmail"))
            {
                throw new FeedbackNotificationException("Feedback delivery is disabled in this environment.");
            }
            var senderAddress = RequireSetting(_configuration, SenderSettingName);
            var recipientAddress = RequireSetting(_configuration, RecipientSettingName);
            if (!System.Net.Mail.MailAddress.TryCreate(recipientAddress, out var recipient) ||
                !string.Equals(recipient.Address, recipientAddress, StringComparison.Ordinal))
                throw new InvalidOperationException("Feedback recipient must be one mailbox address.");
            var emailClient = CreateClient(_configuration);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            var htmlContent = $"""
                <html>
                  <body>
                    <pre style="font-family: Segoe UI, Arial, sans-serif; white-space: pre-wrap;">{WebUtility.HtmlEncode(body)}</pre>
                  </body>
                </html>
                """;

            var operation = await emailClient.SendAsync(
                WaitUntil.Started,
                senderAddress,
                recipientAddress,
                subject,
                htmlContent,
                plainTextContent: body,
                timeout.Token);

            return new FeedbackNotification(operation.Id);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeedbackNotificationException("Feedback delivery timed out; delivery status is unknown.", ex);
        }
        catch (RequestFailedException ex)
        {
            throw new FeedbackNotificationException("Azure Communication Services failed to queue the feedback email.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new FeedbackNotificationException("Feedback email delivery is not configured correctly.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new FeedbackNotificationException("Feedback email delivery is not configured correctly.", ex);
        }
        catch (CredentialUnavailableException ex)
        {
            throw new FeedbackNotificationException("Feedback email delivery could not authenticate with the managed identity.", ex);
        }
        catch (AuthenticationFailedException ex)
        {
            throw new FeedbackNotificationException("Feedback email delivery could not authenticate with the managed identity.", ex);
        }
    }

    private static EmailClient CreateClient(IConfiguration configuration)
    {
        var endpoint = RequireSetting(configuration, EndpointSettingName);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{EndpointSettingName} must be an absolute HTTPS URI.");
        }

        var options = new EmailClientOptions();
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
        return new EmailClient(endpointUri, GalleryCredential.Create(configuration), options);
    }

    private static string RequireSetting(IConfiguration configuration, string name)
    {
        var value = configuration[name]?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} app setting is not configured.")
            : value;
    }
}
