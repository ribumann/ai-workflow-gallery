using Microsoft.Extensions.Configuration;
using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class FeedbackEmailClientTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("one@example.invalid,two@example.invalid")]
    public async Task Send_RequiresOneExplicitRecipient_WithoutNetwork(string? recipient)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EnableFeedbackEmail"] = "true",
            ["FeedbackEmailSenderAddress"] = "DoNotReply@example.invalid",
            ["FeedbackEmailRecipientAddress"] = recipient,
        }).Build();
        var client = new AzureCommunicationFeedbackEmailClient(config);
        var error = await Assert.ThrowsAsync<FeedbackNotificationException>(() => client.SendAsync("Title", "Private feedback"));
        Assert.Contains("not configured correctly", error.Message);
    }

    [Fact]
    public async Task Send_IsDisabledByDefault()
    {
        var client = new AzureCommunicationFeedbackEmailClient(new ConfigurationBuilder().Build());
        var error = await Assert.ThrowsAsync<FeedbackNotificationException>(() => client.SendAsync("Title", "Feedback"));
        Assert.Contains("disabled", error.Message);
    }
}
