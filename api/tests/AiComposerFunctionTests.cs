using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Smoke tests for `POST /api/ai/compose`.</summary>
public class AiComposerFunctionTests
{
    private static readonly User TestUser = new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = "entra-ai-composer",
        DisplayName = "Test User",
    };

    private static IConfiguration ConfigWithComposer(bool enabled = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EnableAiComposer"] = enabled ? "true" : "false" })
            .Build();

    private static ComposeResponseDto SampleResponse() => new(
        "Suggested Workflow",
        "Problem statement",
        "Desired outcome",
        new List<ProposedStepDto>(),
        new List<GapDto>());

    private AiComposer CreateFunction(
        AppDbContext db,
        FakeAiComposerService? aiComposerService = null,
        FakeAiComposerRateLimiter? rateLimiter = null,
        bool enableAiComposer = true) =>
        new(
            db,
            new FakeCurrentUserService(TestUser),
            aiComposerService ?? FakeAiComposerService.Succeeding(SampleResponse()),
            rateLimiter ?? FakeAiComposerRateLimiter.Allowing(),
            ConfigWithComposer(enableAiComposer),
            NullLogger<AiComposer>.Instance);

    [Fact]
    public async Task Compose_ReturnsNotFound_WhenFeatureDisabled()
    {
        await using var db = TestDbContextFactory.Create();
        var function = CreateFunction(db, enableAiComposer: false);
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"Summarize feedback"}""");

        var result = await function.Compose(request, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Compose_RejectsOversizedBodyBeforeInference()
    {
        await using var db = TestDbContextFactory.Create();
        var result = Assert.IsType<ObjectResult>(await CreateFunction(db).Compose(
            TestHttpRequestFactory.CreateWithJsonBody(new string(' ', 8193)), CancellationToken.None));
        Assert.Equal(413, result.StatusCode);
        Assert.Empty(db.AiComposerRequestLogs);
    }

    [Fact]
    public async Task Compose_SelectsBoundedCandidatesFromLargeCatalog()
    {
        await using var db = TestDbContextFactory.Create();
        for (var i = 0; i < 201; i++)
            db.Tasks.Add(new TaskItem
            {
                Id = Guid.NewGuid(), Name = $"Task {i}", Description = "Metadata",
                TaskType = "prompt", TargetIds = new() { "microsoft.copilot.m365" },
                Tags = new List<string>(),
            });
        await db.SaveChangesAsync();
        var result = Assert.IsType<OkObjectResult>(await CreateFunction(db).Compose(
            TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"A goal"}"""), CancellationToken.None));
        Assert.Equal(200, result.StatusCode);
        Assert.True(Assert.Single(db.AiComposerRequestLogs).Success);
    }

    [Fact]
    public async Task Compose_ReturnsBadRequest_WhenGoalMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = CreateFunction(db);
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"   "}""");

        var result = await function.Compose(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Compose_ReturnsBadRequest_WhenGoalTooLong()
    {
        await using var db = TestDbContextFactory.Create();
        var function = CreateFunction(db);
        var tooLong = new string('a', 1001);
        var request = TestHttpRequestFactory.CreateWithJsonBody($$"""{"goal":"{{tooLong}}"}""");

        var result = await function.Compose(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Compose_ReturnsTooManyRequests_WhenRateLimited()
    {
        await using var db = TestDbContextFactory.Create();
        var function = CreateFunction(db, rateLimiter: FakeAiComposerRateLimiter.Blocking(42));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"Summarize feedback"}""");

        var result = await function.Compose(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, objectResult.StatusCode);
        var body = Assert.IsType<ComposeRateLimitedDto>(objectResult.Value);
        Assert.Equal(42, body.RetryAfterSeconds);
    }

    [Fact]
    public async Task Compose_ReturnsBadGateway_AndLogsFailure_WhenAiComposerServiceUnavailable()
    {
        await using var db = TestDbContextFactory.Create();
        var function = CreateFunction(db, aiComposerService: FakeAiComposerService.Failing());
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"Summarize feedback"}""");

        var result = await function.Compose(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);

        var log = Assert.Single(db.AiComposerRequestLogs);
        Assert.False(log.Success);
        Assert.Equal("Summarize feedback".Length, log.GoalLength);
    }

    [Fact]
    public async Task Compose_ReturnsOk_AndLogsSuccess_WithoutStoringGoalText()
    {
        await using var db = TestDbContextFactory.Create();
        var response = SampleResponse();
        var function = CreateFunction(db, aiComposerService: FakeAiComposerService.Succeeding(response));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"goal":"Summarize feedback"}""");

        var result = await function.Compose(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);

        var log = Assert.Single(db.AiComposerRequestLogs);
        Assert.True(log.Success);
        Assert.Equal(TestUser.Id, log.UserId);
        Assert.Equal("Summarize feedback".Length, log.GoalLength);
        Assert.Null(log.FailureReason);
    }
}
