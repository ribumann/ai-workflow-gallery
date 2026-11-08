using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Smoke tests for `POST /api/feedback`.</summary>
public class FeedbackFunctionTests
{
    private static IConfiguration Config(bool enabled = true) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["EnableFeedbackEmail"] = enabled.ToString() }).Build();
    private static readonly User TestUser = new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = "entra-1",
        DisplayName = "Test User",
    };

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenMessageMissing()
    {
        var function = new Feedback(
            new FakeCurrentUserService(TestUser),
            FakeFeedbackNotificationClient.Succeeding(),
            NullLogger<Feedback>.Instance, Config());

        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"message":"   "}""");

        var result = await function.Submit(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Submit_ReturnsBadRequest_WhenMessageTooLong()
    {
        var function = new Feedback(
            new FakeCurrentUserService(TestUser),
            FakeFeedbackNotificationClient.Succeeding(),
            NullLogger<Feedback>.Instance, Config());

        var tooLong = new string('a', 2001);
        var request = TestHttpRequestFactory.CreateWithJsonBody($$"""{"message":"{{tooLong}}"}""");

        var result = await function.Submit(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Submit_ReturnsAccepted_WhenEmailNotificationSucceeds()
    {
        var notificationClient = FakeFeedbackNotificationClient.Succeeding("email-operation-42");
        var function = new Feedback(
            new FakeCurrentUserService(TestUser),
            notificationClient,
            NullLogger<Feedback>.Instance, Config());

        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"message":"Please add dark mode.","pageContext":"/search"}""");

        var result = await function.Submit(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(202, objectResult.StatusCode);
        var body = Assert.IsType<FeedbackResponseDto>(objectResult.Value);
        Assert.Equal("email-operation-42", body.OperationId);
        Assert.Contains("Please add dark mode.", notificationClient.Body);
        Assert.Contains("Submitted by: Test User", notificationClient.Body);
        Assert.Contains("Page: /search", notificationClient.Body);
    }

    [Fact]
    public async Task Submit_ReturnsBadGateway_WhenEmailNotificationFails()
    {
        var function = new Feedback(
            new FakeCurrentUserService(TestUser),
            FakeFeedbackNotificationClient.Failing(),
            NullLogger<Feedback>.Instance, Config());

        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"message":"Please add dark mode."}""");

        var result = await function.Submit(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }

    [Fact]
    public async Task Submit_Disabled_DoesNotSend()
    {
        var client = FakeFeedbackNotificationClient.Succeeding();
        var function = new Feedback(new FakeCurrentUserService(TestUser), client, NullLogger<Feedback>.Instance, Config(false));
        var result = Assert.IsType<ObjectResult>(await function.Submit(
            TestHttpRequestFactory.CreateWithJsonBody("""{"message":"Keep this private"}"""), CancellationToken.None));
        Assert.Equal(503, result.StatusCode);
        Assert.Null(client.Body);
    }

    [Theory]
    [InlineData(32769, 413)]
    [InlineData(2049, 400)]
    public async Task Submit_RejectsOversizedInputWithoutSending(int length, int status)
    {
        var client = FakeFeedbackNotificationClient.Succeeding();
        var function = new Feedback(new FakeCurrentUserService(TestUser), client, NullLogger<Feedback>.Instance, Config());
        var body = length == 32769 ? new string(' ', length) : $$"""{"message":"Feedback","pageContext":"{{new string('a', length)}}"}""";
        var result = await function.Submit(TestHttpRequestFactory.CreateWithJsonBody(body), CancellationToken.None);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        Assert.Null(client.Body);
    }
}
