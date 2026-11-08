using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class PublicDemoPolicyTests
{
    private static readonly PublicDemoPolicy Public = new(true);
    private static IConfiguration Configuration(params (string Name, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Name, x => x.Value)).Build();

    [Fact]
    public void Configuration_IsExplicitAndFailClosed()
    {
        Assert.False(PublicDemoPolicy.FromConfiguration(Configuration()).Enabled);
        Assert.False(PublicDemoPolicy.FromConfiguration(Configuration(("PublicDemoEnabled", "false"))).Enabled);
        Assert.True(PublicDemoPolicy.FromConfiguration(Configuration(("PublicDemoEnabled", "true"))).Enabled);
        foreach (var invalid in new[] { "", "1", "yes", "enabled" })
            Assert.Throws<InvalidOperationException>(() => PublicDemoPolicy.FromConfiguration(Configuration(("PublicDemoEnabled", invalid))));
    }

    [Theory]
    [InlineData("GET", "/api/session", "", true)]
    [InlineData("GET", "/api/catalog", "?collectionId=public-techniques", true)]
    [InlineData("GET", "/api/tasks", "?collectionId=public-techniques", true)]
    [InlineData("GET", "/api/workflows", "?collectionId=curated-tools", true)]
    [InlineData("GET", "/api/methods", "?collectionId=public-techniques", true)]
    [InlineData("GET", "/api/reports", "?collectionId=curated-tools", true)]
    [InlineData("GET", "/api/tasks/11111111-1111-4111-8111-111111111111/file", "?collectionId=public-techniques", true)]
    [InlineData("GET", "/api/tasks", "", false)]
    [InlineData("GET", "/api/tasks", "?collectionId=unknown", false)]
    [InlineData("GET", "/api/tasks", "?collectionId=public-techniques&collectionId=curated-tools", false)]
    [InlineData("POST", "/api/tasks", "?collectionId=public-techniques", false)]
    [InlineData("PUT", "/api/workflows/11111111-1111-4111-8111-111111111111", "?collectionId=public-techniques", false)]
    [InlineData("DELETE", "/api/methods/11111111-1111-4111-8111-111111111111", "?collectionId=public-techniques", false)]
    [InlineData("POST", "/api/feedback", "", false)]
    [InlineData("POST", "/api/ai/compose", "?collectionId=public-techniques", false)]
    [InlineData("POST", "/api/mcp", "", false)]
    [InlineData("GET", "/api/mcp", "", false)]
    [InlineData("GET", "/api/tasks/11111111-1111-4111-8111-111111111111/comments", "?collectionId=public-techniques", false)]
    [InlineData("GET", "/api/tasks/11111111-1111-4111-8111-111111111111/rating", "?collectionId=public-techniques", false)]
    [InlineData("GET", "/api/oauth-resource-metadata", "", false)]
    [InlineData("GET", "/api/unknown", "?collectionId=public-techniques", false)]
    public async Task Boundary_AllowsOnlyExplicitScopedReadsWithoutUsingEntra(string method, string path, string query, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new(query);
        context.Response.Body = new MemoryStream();
        Principal(context.Request, User());
        var validator = new NeverValidate();
        var authorization = new GalleryRequestAuthorization(Configuration(), validator,
            NullLogger<GalleryRequestAuthorization>.Instance, Public);
        Assert.Equal(allowed, await authorization.AuthorizeAsync(context, Ct));
        Assert.Equal(0, validator.Calls);
        Assert.False(context.User.Identity?.IsAuthenticated);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        if (!allowed) Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task PublicSession_IsAnonymousAndComposerDisabled()
    {
        var request = Request("/api/session");
        Principal(request, User("Must not become public identity"));
        var result = Assert.IsType<OkObjectResult>(await new GetSession(Public).Run(request));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.False(json.GetProperty("isAuthenticated").GetBoolean());
        Assert.False(json.GetProperty("isAuthorized").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("displayName").ValueKind);
        Assert.True(json.GetProperty("publicDemo").GetBoolean());
        Assert.False(json.GetProperty("composerEnabled").GetBoolean());
    }

    [Fact]
    public async Task PublicHostedReads_ExcludeUnpublishedRowsAndNavigationSnapshots()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        var hiddenMethod = Method(user, "Private method title");
        var visible = Task(user, "Published prompt", true);
        visible.MethodId = hiddenMethod.Id;
        visible.AttachedFileBlobPath = "published-file";
        visible.AttachedFileName = "public.md";
        var hidden = Task(user, "Private task title", false);
        var visibleWorkflow = Workflow(user, "Published workflow", true);
        visibleWorkflow.MethodId = hiddenMethod.Id;
        visibleWorkflow.Steps = new List<WorkflowStep>
        {
            Step(visibleWorkflow, visible, 1),
            Step(visibleWorkflow, hidden, 2),
            new() { Id = Guid.NewGuid(), WorkflowId = visibleWorkflow.Id, SequenceOrder = 3, TargetId = "github.copilot.vscode",
                TaskNameSnapshot = "Private deleted snapshot", TaskTypeSnapshot = "agent" }
        };
        var hiddenWorkflow = Workflow(user, "Private workflow title", false);
        hiddenWorkflow.Steps = new List<WorkflowStep> { Step(hiddenWorkflow, visible, 1) };
        var visibleReport = Report(user, "Published report", true);
        var hiddenReport = Report(user, "Private report title", false);
        db.Users.Add(user);
        db.AiMethods.Add(hiddenMethod);
        db.Tasks.AddRange(visible, hidden);
        db.Workflows.AddRange(visibleWorkflow, hiddenWorkflow);
        db.ReportLinks.AddRange(visibleReport, hiddenReport);
        await db.SaveChangesAsync();
        db.CollectionId = "curated-tools";
        var curated = Task(user, "Curated prompt", true);
        db.Tasks.Add(curated);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using var server = Server(db);
        using var client = server.CreateClient();
        var taskJson = await Json(client, $"/api/tasks/{visible.Id}?collectionId=public-techniques");
        Assert.Equal(JsonValueKind.Null, taskJson.GetProperty("methodId").ValueKind);
        Assert.Equal(visibleWorkflow.Id, Assert.Single(taskJson.GetProperty("referencedByWorkflowIds").EnumerateArray()).GetGuid());
        var workflowJson = await Json(client, $"/api/workflows/{visibleWorkflow.Id}?collectionId=public-techniques");
        Assert.Equal(JsonValueKind.Null, workflowJson.GetProperty("methodId").ValueKind);
        var steps = workflowJson.GetProperty("steps").EnumerateArray().ToArray();
        Assert.True(steps[0].GetProperty("taskAvailable").GetBoolean());
        foreach (var unavailable in steps.Skip(1))
        {
            Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("taskId").ValueKind);
            Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("taskType").ValueKind);
            Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("targetId").ValueKind);
            Assert.Equal("Unavailable task", unavailable.GetProperty("taskName").GetString());
            Assert.False(unavailable.GetProperty("taskAvailable").GetBoolean());
            Assert.Empty(unavailable.GetProperty("taskTargetIds").EnumerateArray());
        }
        Assert.DoesNotContain("Private", workflowJson.GetRawText());
        var list = await Json(client, "/api/tasks?collectionId=public-techniques");
        Assert.Equal(visible.Id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var curatedList = await Json(client, "/api/tasks?collectionId=curated-tools");
        Assert.Equal(curated.Id, Assert.Single(curatedList.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        foreach (var path in new[] { $"tasks/{hidden.Id}", $"tasks/{hidden.Id}/file", $"workflows/{hiddenWorkflow.Id}",
                     $"methods/{hiddenMethod.Id}", $"reports/{hiddenReport.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/{path}?collectionId=public-techniques")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync($"/api/tasks/{visible.Id}/file?collectionId=public-techniques")).StatusCode);
        var workflows = await Json(client, "/api/workflows?collectionId=public-techniques&taskType=agent");
        Assert.Empty(workflows.GetProperty("items").EnumerateArray());
        var reports = await Json(client, "/api/reports?collectionId=public-techniques");
        Assert.Equal(visibleReport.Id, Assert.Single(reports.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await Json(client, "/api/methods?collectionId=public-techniques")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/tasks?collectionId=public-techniques", new StringContent("{}"))).StatusCode);
        db.PublicOnly = false;
        Assert.Equal(3, await db.Tasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task DirectMutationAndPaidHandlers_AreDeniedBeforeSideEffects()
    {
        await using var db = TestDbContextFactory.Create();
        var current = new FakeCurrentUserService(null);
        var tasks = new Tasks(db, current, new DisabledBlobStorageService(), new FakeFileValidationService());
        var methods = new Methods(db, current);
        var reports = new Reports(db, current);
        var workflows = new Workflows(db, current);
        var deniedWrites = new[]
        {
            ("task create", await tasks.Create(Request("/api/tasks", "POST"), Ct)),
            ("task update", await tasks.Update(Request("/api/tasks", "PUT"), Guid.NewGuid(), Ct)),
            ("task delete", await tasks.Delete(Request("/api/tasks", "DELETE"), Guid.NewGuid(), Ct)),
            ("method create", await methods.Create(Request("/api/methods", "POST"), Ct)),
            ("method update", await methods.Update(Request("/api/methods", "PUT"), Guid.NewGuid(), Ct)),
            ("method delete", await methods.Delete(Request("/api/methods", "DELETE"), Guid.NewGuid(), Ct)),
            ("report create", await reports.Create(Request("/api/reports", "POST"), Ct)),
            ("report update", await reports.Update(Request("/api/reports", "PUT"), Guid.NewGuid(), Ct)),
            ("report delete", await reports.Delete(Request("/api/reports", "DELETE"), Guid.NewGuid(), Ct)),
            ("workflow create", await workflows.Create(Request("/api/workflows", "POST"), Ct)),
            ("workflow update", await workflows.Update(Request("/api/workflows", "PUT"), Guid.NewGuid(), Ct)),
            ("workflow delete", await workflows.Delete(Request("/api/workflows", "DELETE"), Guid.NewGuid(), Ct)),
        };
        foreach (var (operation, result) in deniedWrites)
            Assert.True(result is ObjectResult { StatusCode: 403 }, $"{operation} returned {result.GetType().Name}.");
        var composer = new AiComposer(db, current, FakeAiComposerService.Failing(), FakeAiComposerRateLimiter.Allowing(),
            Configuration(("EnableAiComposer", "true")), NullLogger<AiComposer>.Instance, Public);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await composer.Compose(Request("/api/ai/compose", "POST"), Ct)).StatusCode);
        var feedback = new Feedback(current, FakeFeedbackNotificationClient.Failing(), NullLogger<Feedback>.Instance,
            Configuration(("EnableFeedbackEmail", "true")), Public);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await feedback.Submit(Request("/api/feedback", "POST"), Ct)).StatusCode);
        var engagement = new Engagement(db, current);
        var itemId = Guid.NewGuid();
        Assert.Equal(403, Assert.IsType<ObjectResult>(await engagement.GetTaskRating(
            Request($"/api/tasks/{itemId}/rating"), itemId, Ct)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await engagement.ListTaskComments(
            Request($"/api/tasks/{itemId}/comments"), itemId, Ct)).StatusCode);
        var mcp = McpTestHost.Create(db, null).Endpoint;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await mcp.Post(Request("/api/mcp", "POST"), Ct)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(mcp.Get(Request("/api/mcp"))).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(mcp.Delete(Request("/api/mcp", "DELETE"))).StatusCode);
        Assert.Empty(db.Users);
        Assert.Empty(db.AiComposerRequestLogs);
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public void PublicCapabilities_GrantWritesAndEngagementOnlyToEnabledPlayground()
    {
        var enabled = new PublicDemoPolicy(true, playgroundWritesEnabled: true);
        var disabled = new PublicDemoPolicy(true);
        foreach (var collection in new[] { "public-techniques", "curated-tools" })
        {
            Assert.False(CollectionCapabilities.For(collection, enabled).CanWrite);
            Assert.False(CollectionCapabilities.For(collection, enabled).CanReadEngagement);
        }
        Assert.True(CollectionCapabilities.For("playground", enabled).CanWrite);
        Assert.True(CollectionCapabilities.For("playground", enabled).CanReadEngagement);
        Assert.False(CollectionCapabilities.For("playground", disabled).CanWrite);

        foreach (var (method, path) in new[]
        {
            ("POST", "/api/tasks"),
            ("PUT", "/api/tasks/11111111-1111-4111-8111-111111111111"),
            ("DELETE", "/api/tasks/11111111-1111-4111-8111-111111111111"),
            ("POST", "/api/workflows"),
            ("PUT", "/api/methods/11111111-1111-4111-8111-111111111111"),
            ("DELETE", "/api/reports/11111111-1111-4111-8111-111111111111"),
            ("POST", "/api/tasks/11111111-1111-4111-8111-111111111111/comments"),
            ("PUT", "/api/tasks/11111111-1111-4111-8111-111111111111/rating"),
        })
        {
            Assert.True(WriteRequest(enabled, method, path, "playground"));
            Assert.False(WriteRequest(enabled, method, path, "public-techniques"));
            Assert.False(WriteRequest(enabled, method, path, "curated-tools"));
        }
        Assert.False(WriteRequest(enabled, "POST", "/api/tasks", null));
        Assert.False(WriteRequest(enabled, "POST", "/api/tasks", "playground", duplicateScope: true));
    }

    [Fact]
    public async Task PlaygroundCurrentUser_UsesAnonymousSandboxActorWithoutEntraClaims()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var actor = new User
        {
            Id = CurrentUserService.SandboxActorId,
            DisplayName = "Playground contributor",
            EntraTenantId = null,
            EntraObjectId = null,
            IsSandboxActor = true,
        };
        db.Users.Add(actor);
        await db.SaveChangesAsync();

        var policy = new PublicDemoPolicy(true, playgroundWritesEnabled: true);
        var request = new DefaultHttpContext().Request;
        request.Method = "POST";
        request.Path = "/api/tasks";
        request.QueryString = new("?collectionId=playground");
        policy.Stamp(request.HttpContext);
        Assert.Null(CollectionScope.Apply(request, db, write: true));

        var resolved = await new CurrentUserService(db).GetOrCreateCurrentUserAsync(request, Ct);
        Assert.Same(actor, resolved);
        Assert.Null(resolved!.EntraTenantId);
        Assert.Null(resolved.EntraObjectId);
        Assert.False(request.HttpContext.User.Identity?.IsAuthenticated);
    }

    private static bool WriteRequest(
        PublicDemoPolicy policy,
        string method,
        string path,
        string? collection,
        bool duplicateScope = false)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = path;
        if (collection is not null)
            request.QueryString = new(duplicateScope
                ? $"?collectionId={collection}&collectionId=playground"
                : $"?collectionId={collection}");
        policy.Stamp(request.HttpContext);
        return policy.AllowsWrite(request);
    }

    private static HttpRequest Request(string path, string method = "GET")
    {
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = path;
        request.QueryString = new("?collectionId=public-techniques");
        Public.Stamp(request.HttpContext);
        return request;
    }

    private static TestServer Server(AppDbContext db)
    {
        var authorization = new GalleryRequestAuthorization(Configuration(), new NeverValidate(),
            NullLogger<GalleryRequestAuthorization>.Instance, Public);
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddControllers();
                services.AddSingleton(db);
                services.AddSingleton(Public);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (await authorization.AuthorizeAsync(context, context.RequestAborted)) await next();
                });
                app.Run(async context =>
                {
                    var segments = context.Request.Path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var id = segments.Length >= 3 && Guid.TryParse(segments[2], out var parsed) ? parsed : (Guid?)null;
                    var current = new FakeCurrentUserService(null);
                    IActionResult result = segments[1] switch
                    {
                        "session" => await new GetSession(Public).Run(context.Request),
                        "catalog" => new Catalog().Get(context.Request),
                        "tasks" when segments.Length == 4 => await new Tasks(db, current, new DisabledBlobStorageService(), new FakeFileValidationService()).GetFile(context.Request, id!.Value, Ct),
                        "tasks" when id is not null => await new Tasks(db, current, new DisabledBlobStorageService(), new FakeFileValidationService()).GetById(context.Request, id.Value, Ct),
                        "tasks" => await new Tasks(db, current, new DisabledBlobStorageService(), new FakeFileValidationService()).List(context.Request, Ct),
                        "workflows" when id is not null => await new Workflows(db, current).GetById(context.Request, id.Value, Ct),
                        "workflows" => await new Workflows(db, current).List(context.Request, Ct),
                        "methods" when id is not null => await new Methods(db, current).GetById(context.Request, id.Value, Ct),
                        "methods" => await new Methods(db, current).List(context.Request, Ct),
                        "reports" when id is not null => await new Reports(db, current).GetById(context.Request, id.Value, Ct),
                        "reports" => await new Reports(db, current).List(context.Request, Ct),
                        _ => new NotFoundResult()
                    };
                    await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
                });
            }));
    }

    private static async Task<JsonElement> Json(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
    }
    private static TaskItem Task(User user, string name, bool published) => new()
    {
        Id = Guid.NewGuid(), Name = name, Description = name, TaskType = "prompt",
        TargetIds = new() { "microsoft.copilot.m365" }, InlineContent = "Supplied content",
        CreatedByUserId = user.Id, LastEditedByUserId = user.Id, IsPublished = published,
    };
    private static Workflow Workflow(User user, string name, bool published) => new()
    {
        Id = Guid.NewGuid(), Name = name, ProblemStatement = "Problem", DesiredOutcome = "Outcome",
        CreatedByUserId = user.Id, LastEditedByUserId = user.Id, IsPublished = published,
    };
    private static WorkflowStep Step(Workflow workflow, TaskItem task, int order) => new()
    {
        Id = Guid.NewGuid(), WorkflowId = workflow.Id, TaskId = task.Id, SequenceOrder = order,
        TargetId = "microsoft.copilot.m365", TaskNameSnapshot = task.Name, TaskTypeSnapshot = task.TaskType,
    };
    private static ReportLink Report(User user, string name, bool published) => new()
    {
        Id = Guid.NewGuid(), Name = name, Description = name, Url = "https://example.com/report",
        Kind = ReportLinkKind.Report, CreatedByUserId = user.Id, LastEditedByUserId = user.Id,
        IsPublished = published, Version = Guid.NewGuid(),
    };
    private sealed class NeverValidate : IEntraTokenValidator
    {
        public int Calls { get; private set; }
        public Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Public requests must not initialize Entra validation.");
        }
    }
}
