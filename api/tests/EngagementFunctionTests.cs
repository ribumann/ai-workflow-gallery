using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Smoke tests for `/api/tasks/{id}/rating` and `/api/tasks/{id}/comments`.</summary>
public class EngagementFunctionTests
{
    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = $"entra-{Guid.NewGuid()}",
        DisplayName = "Test User",
    };

    private static TaskItem NewTask(User user) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Sample Agent",
        Description = "A sample agent.",
        TaskType = "agent",
        TargetIds = new() { "github.copilot.vscode" },
        ReferenceLink = "https://example.com",
        CreatedByUser = user,
        CreatedByUserId = user.Id,
        LastEditedByUser = user,
        LastEditedByUserId = user.Id,
    };

    [Fact]
    public async Task GetTaskRating_ReturnsNotFound_WhenTaskMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Engagement(db, new FakeCurrentUserService(null));

        var result = await function.GetTaskRating(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RateTask_ReturnsBadRequest_WhenDeviceIdMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"value":1}""");

        var result = await function.RateTask(request, task.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RateTask_ReturnsBadRequest_WhenValueInvalid()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":5}""");

        var result = await function.RateTask(request, task.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RateTask_ReturnsNotFound_WhenTaskMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var function = new Engagement(db, new FakeCurrentUserService(user));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}""");

        var result = await function.RateTask(request, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RateTask_AddsVote_ThenTogglesOff_WhenSameValueRepeated()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        var firstVote = await function.RateTask(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            task.Id,
            CancellationToken.None);
        var firstOk = Assert.IsType<OkObjectResult>(firstVote);
        var firstSummary = Assert.IsType<RatingSummaryDto>(firstOk.Value);
        Assert.Equal(1, firstSummary.HelpfulCount);
        Assert.Equal(0, firstSummary.NotHelpfulCount);
        Assert.Equal(1, firstSummary.MyVote);

        var secondVote = await function.RateTask(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            task.Id,
            CancellationToken.None);
        var secondOk = Assert.IsType<OkObjectResult>(secondVote);
        var secondSummary = Assert.IsType<RatingSummaryDto>(secondOk.Value);
        Assert.Equal(0, secondSummary.HelpfulCount);
        Assert.Equal(0, secondSummary.NotHelpfulCount);
        Assert.Null(secondSummary.MyVote);
        Assert.Empty(await db.Ratings.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RateTask_SwitchesVote_WhenDifferentValueSubmitted()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        await function.RateTask(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            task.Id,
            CancellationToken.None);

        var switched = await function.RateTask(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":-1}"""),
            task.Id,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(switched);
        var summary = Assert.IsType<RatingSummaryDto>(ok.Value);
        Assert.Equal(0, summary.HelpfulCount);
        Assert.Equal(1, summary.NotHelpfulCount);
        Assert.Equal(-1, summary.MyVote);
        Assert.Single(await db.Ratings.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListTaskComments_ReturnsNotFound_WhenTaskMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Engagement(db, new FakeCurrentUserService(null));

        var result = await function.ListTaskComments(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AddTaskComment_ReturnsBadRequest_WhenBodyMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"body":"   "}""");

        var result = await function.AddTaskComment(request, task.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddTaskComment_ReturnsBadRequest_WhenBodyTooLong()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));
        var tooLong = new string('a', 1001);
        var request = TestHttpRequestFactory.CreateWithJsonBody($$"""{"body":"{{tooLong}}"}""");

        var result = await function.AddTaskComment(request, task.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddTaskComment_ReturnsCreated_AndIsListedAfterward()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));
        var request = TestHttpRequestFactory.CreateWithJsonBody("""{"body":"This is really useful, thanks!"}""");

        var result = await function.AddTaskComment(request, task.Id, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<CommentDto>(created.Value);
        Assert.Equal("This is really useful, thanks!", dto.Body);
        Assert.Equal("Test User", dto.CreatedByDisplayName);

        var listResult = await function.ListTaskComments(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            task.Id,
            CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult);
        var list = Assert.IsType<CommentListDto>(listOk.Value);
        Assert.Single(list.Items);
    }

    private static AiMethod NewMethod(User user) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Prime before you prompt",
        Summary = "Gather context first, then metaprompt.",
        Approach = "## Steps\n\n1. Gather the reports.\n2. Prime the model.",
        CreatedByUser = user,
        CreatedByUserId = user.Id,
        LastEditedByUser = user,
        LastEditedByUserId = user.Id,
        Version = Guid.NewGuid(),
    };

    [Fact]
    public async Task GetMethodRating_ReturnsNotFound_WhenMethodMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Engagement(db, new FakeCurrentUserService(null));

        var result = await function.GetMethodRating(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RateMethod_AddsVote_ThenTogglesOff_WhenSameValueRepeated()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var method = NewMethod(user);
        db.Users.Add(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        var first = await function.RateMethod(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            method.Id,
            CancellationToken.None);
        var firstSummary = Assert.IsType<RatingSummaryDto>(Assert.IsType<OkObjectResult>(first).Value);
        Assert.Equal(1, firstSummary.HelpfulCount);
        Assert.Equal(1, firstSummary.MyVote);

        var second = await function.RateMethod(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            method.Id,
            CancellationToken.None);
        var secondSummary = Assert.IsType<RatingSummaryDto>(Assert.IsType<OkObjectResult>(second).Value);
        Assert.Equal(0, secondSummary.HelpfulCount);
        Assert.Null(secondSummary.MyVote);
    }

    [Fact]
    public async Task RateMethod_DoesNotCollideWithSameIdOnAnotherSubjectType()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var method = NewMethod(user);
        var task = NewTask(user);
        task.Id = method.Id;
        db.Users.Add(user);
        db.AiMethods.Add(method);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        await function.RateMethod(
            TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"device-1","value":1}"""),
            method.Id,
            CancellationToken.None);

        var taskRating = await function.GetTaskRating(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["deviceId"] = "device-1",
            }),
            task.Id,
            CancellationToken.None);

        var summary = Assert.IsType<RatingSummaryDto>(Assert.IsType<OkObjectResult>(taskRating).Value);
        Assert.Equal(0, summary.HelpfulCount);
        Assert.Null(summary.MyVote);
    }

    [Fact]
    public async Task AddMethodComment_ReturnsNotFound_WhenMethodMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        var result = await function.AddMethodComment(
            TestHttpRequestFactory.CreateWithJsonBody("""{"body":"Nice approach."}"""),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AddMethodComment_ReturnsCreated_AndIsListedAfterward()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var method = NewMethod(user);
        db.Users.Add(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var function = new Engagement(db, new FakeCurrentUserService(user));

        var result = await function.AddMethodComment(
            TestHttpRequestFactory.CreateWithJsonBody("""{"body":"We use this for weekly reporting."}"""),
            method.Id,
            CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<CommentDto>(created.Value);
        Assert.Equal("We use this for weekly reporting.", dto.Body);

        var listResult = await function.ListMethodComments(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            method.Id,
            CancellationToken.None);
        var list = Assert.IsType<CommentListDto>(Assert.IsType<OkObjectResult>(listResult).Value);
        Assert.Single(list.Items);
    }
}
