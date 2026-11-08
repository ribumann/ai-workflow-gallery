using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class PublicPlaygroundRestTests
{
    [Fact]
    public async Task PlaygroundTaskUploadIsAttributedScopedVisibleAndDeletable()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var policy = new PublicDemoPolicy(true, playgroundWritesEnabled: true);
        db.Users.Add(SandboxActor());
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        var tasks = new Tasks(db, new CurrentUserService(db), blobs, new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithForm(
            new Dictionary<string, StringValues>
            {
                ["metadata"] = """{"name":"Disposable draft","description":"Playground test","taskType":"prompt","targetIds":["microsoft.copilot.m365"],"inlineContent":"Draft text"}""",
            },
            new FormFileCollection { TestHttpRequestFactory.CreateFormFile("file", "draft.md", "Attachment") });
        request.Method = HttpMethods.Post;
        request.Path = "/api/tasks";
        request.QueryString = new QueryString("?collectionId=playground");
        policy.Stamp(request.HttpContext);

        var created = Assert.IsType<CreatedResult>(await tasks.Create(request, Ct));
        var dto = Assert.IsType<TaskDetailDto>(created.Value);
        Assert.Contains("collectionId=playground", created.Location);
        Assert.False(request.HttpContext.User.Identity?.IsAuthenticated);
        Assert.Equal("playground", Assert.Single(blobs.UploadedCollectionIds));
        Assert.StartsWith("playground/", Assert.Single(blobs.UploadedBlobPaths));

        var saved = await db.Tasks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(CollectionScope.Playground, saved.CollectionId);
        Assert.False(saved.IsPublished);
        Assert.Equal(CurrentUserService.SandboxActorId, saved.CreatedByUserId);
        Assert.Equal(CurrentUserService.SandboxActorId, saved.LastEditedByUserId);
        Assert.Null(await db.Users.Where(user => user.Id == saved.CreatedByUserId)
            .Select(user => user.EntraObjectId).SingleAsync());

        var listRequest = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
        {
            ["collectionId"] = CollectionScope.Playground,
        });
        listRequest.Path = "/api/tasks";
        policy.Stamp(listRequest.HttpContext);
        var list = Assert.IsType<PagedResultDto<TaskSummaryDto>>(
            Assert.IsType<OkObjectResult>(await tasks.List(listRequest, Ct)).Value);
        Assert.Equal(dto.Id, Assert.Single(list.Items).Id);

        var updateRequest = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = JsonSerializer.Serialize(new
            {
                name = "Reviewed disposable draft",
                description = "Updated Playground test",
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                inlineContent = "Updated draft text",
                expectedLastEditedAt = dto.LastEditedAt,
            }),
        });
        updateRequest.Method = HttpMethods.Put;
        updateRequest.Path = $"/api/tasks/{dto.Id}";
        updateRequest.QueryString = new QueryString("?collectionId=playground");
        policy.Stamp(updateRequest.HttpContext);
        var updated = Assert.IsType<TaskDetailDto>(
            Assert.IsType<OkObjectResult>(await tasks.Update(updateRequest, dto.Id, Ct)).Value);
        Assert.Equal("Reviewed disposable draft", updated.Name);

        var engagement = new Engagement(db, new CurrentUserService(db));
        var commentRequest = TestHttpRequestFactory.CreateWithJsonBody("""{"body":"Disposable Playground comment"}""");
        commentRequest.Method = HttpMethods.Post;
        commentRequest.Path = $"/api/tasks/{dto.Id}/comments";
        commentRequest.QueryString = new QueryString("?collectionId=playground");
        policy.Stamp(commentRequest.HttpContext);
        var addedComment = Assert.IsType<ObjectResult>(
            await engagement.AddTaskComment(commentRequest, dto.Id, Ct));
        Assert.Equal(StatusCodes.Status201Created, addedComment.StatusCode);
        var comment = await db.Comments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(CollectionScope.Playground, comment.CollectionId);
        Assert.Equal(CurrentUserService.SandboxActorId, comment.CreatedByUserId);

        var ratingRequest = TestHttpRequestFactory.CreateWithJsonBody("""{"deviceId":"playground-test","value":1}""");
        ratingRequest.Method = HttpMethods.Put;
        ratingRequest.Path = $"/api/tasks/{dto.Id}/rating";
        ratingRequest.QueryString = new QueryString("?collectionId=playground");
        policy.Stamp(ratingRequest.HttpContext);
        Assert.IsType<OkObjectResult>(await engagement.RateTask(ratingRequest, dto.Id, Ct));
        Assert.Equal(CollectionScope.Playground,
            (await db.Ratings.IgnoreQueryFilters().SingleAsync()).CollectionId);

        var deleteRequest = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
        {
            ["collectionId"] = CollectionScope.Playground,
        });
        deleteRequest.Method = HttpMethods.Delete;
        deleteRequest.Path = $"/api/tasks/{dto.Id}";
        policy.Stamp(deleteRequest.HttpContext);
        Assert.IsType<NoContentResult>(await tasks.Delete(deleteRequest, dto.Id, Ct));
        Assert.Empty(await db.Tasks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task PlaygroundRejectsCrossCollectionMethodReferencesBeforeUploading()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        db.Users.Add(SandboxActor());
        var owner = User();
        db.Users.Add(owner);
        db.AiMethods.Add(Method(owner));
        db.CollectionId = CollectionScope.PublicTechniques;
        await db.SaveChangesAsync();
        var publicMethodId = await db.AiMethods.IgnoreQueryFilters().Select(method => method.Id).SingleAsync();

        var policy = new PublicDemoPolicy(true, playgroundWritesEnabled: true);
        var blobs = new FakeBlobStorageService();
        var tasks = new Tasks(db, new CurrentUserService(db), blobs, new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = JsonSerializer.Serialize(new
            {
                name = "Invalid cross-scope draft",
                description = "Must not reference a published method",
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                inlineContent = "Not saved",
                methodId = publicMethodId,
            }),
        });
        request.Method = HttpMethods.Post;
        request.Path = "/api/tasks";
        request.QueryString = new QueryString("?collectionId=playground");
        policy.Stamp(request.HttpContext);

        Assert.IsType<BadRequestObjectResult>(await tasks.Create(request, Ct));
        Assert.Empty(blobs.UploadedBlobPaths);
        Assert.Empty(await db.Tasks.IgnoreQueryFilters().ToListAsync());
    }

    private static User SandboxActor() => new()
    {
        Id = CurrentUserService.SandboxActorId,
        DisplayName = "Playground contributor",
        IsSandboxActor = true,
        EntraTenantId = null,
        EntraObjectId = null,
    };
}
