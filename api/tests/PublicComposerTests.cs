using System.Text;
using System.Text.Json;
using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class PublicComposerTests
{
    [Theory]
    [InlineData("PublicComposerEnabled", "false", 403, 0)]
    [InlineData("EnableAiComposer", "false", 503, 0)]
    [InlineData("PublicComposerEnabled", "true", 200, 1)]
    public async Task HostedBoundary_OnlyAdmitsScopedProposal_AndKeepsEveryWriteDenied(string key, string value, int status, int calls)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db);
        var config = Config(key, value);
        var model = new Model();
        var function = Function(db, model, config);
        var policy = new PublicDemoPolicy(true);
        var authorization = new GalleryRequestAuthorization(config, new NeverEntra(),
            NullLogger<GalleryRequestAuthorization>.Instance, policy);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddControllers();
                services.AddSingleton(db);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (await authorization.AuthorizeAsync(context, default)) await next();
                });
                app.Run(async context =>
                {
                    var result = await function.Compose(context.Request, default);
                    await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
                });
            }));
        using var client = server.CreateClient();
        foreach (var path in new[] { "tasks", "workflows", "methods", "feedback", "mcp",
                     "tasks/11111111-1111-4111-8111-111111111111/comments" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/" + path +
                "?collectionId=public-techniques", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/ai/compose?collectionId=unknown",
            new StringContent("""{"goal":"evidence"}"""))).StatusCode);
        var response = await client.PostAsync("/api/ai/compose?collectionId=public-techniques",
            new StringContent("""{"goal":"Summarize evidence"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(calls, model.Calls);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Single(db.Users);
        Assert.Empty(db.AiComposerRequestLogs);
    }

    internal static IConfiguration Config(string? setting = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["PublicDemoEnabled"] = "true", ["EnableAiComposer"] = "true", ["PublicComposerEnabled"] = "true",
            ["AiFoundryEndpoint"] = "https://example.openai.azure.com", ["AiFoundryDeploymentName"] = "test",
            ["PublicComposer:DailyRequestLimit"] = "10", ["PublicComposer:DailyTokenLimit"] = "100000",
            ["PublicComposer:MaxConcurrentRequests"] = "1", ["PublicComposer:MaxGoalCharacters"] = "1000",
            ["PublicComposer:MaxContextBytes"] = "12000", ["PublicComposer:MaxOutputTokens"] = "1024",
            ["PublicComposer:ModelTimeoutSeconds"] = "20",
        };
        if (setting is not null) values[setting] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static PublicComposerBudget Budget(AppDbContext db, PublicComposerSettings settings, TimeProvider? clock = null) =>
        new(db, settings, NullLogger<PublicComposerBudget>.Instance, clock);

    internal static HttpRequest Request(string body = """{"goal":"Summarize evidence"}""", string collection = "public-techniques")
    {
        var request = TestHttpRequestFactory.CreateWithJsonBody(body);
        request.Method = "POST";
        request.Path = "/api/ai/compose";
        request.QueryString = new("?collectionId=" + collection);
        new PublicDemoPolicy(true).Stamp(request.HttpContext);
        return request;
    }

    internal static async Task<TaskItem> Seed(AppDbContext db, string name = "Summarize evidence", bool published = true)
    {
        var user = await db.Users.FirstOrDefaultAsync() ?? MethodTestSupport.User();
        if (db.Entry(user).State == EntityState.Detached) db.Users.Add(user);
        var task = new TaskItem
        {
            Id = Guid.NewGuid(), Name = name, Description = "Summarize evidence using public sources.",
            TaskType = "prompt", TargetIds = ["microsoft.copilot.m365"], Tags = ["evidence"],
            IsPublished = published, CreatedByUserId = user.Id, LastEditedByUserId = user.Id,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AiComposer Function(AppDbContext db, Model model, IConfiguration? config = null,
        IPublicComposerBudget? budget = null)
    {
        config ??= Config();
        var settings = PublicComposerSettings.Read(config);
        return new AiComposer(db, new NeverCurrentUser(), model, FakeAiComposerRateLimiter.Allowing(), config,
            NullLogger<AiComposer>.Instance, new(true),
            new PublicComposer(db, settings, budget ?? Budget(db, settings), model, NullLogger<PublicComposer>.Instance));
    }

    [Theory]
    [InlineData("PublicComposerEnabled", null, 403)]
    [InlineData("PublicComposerEnabled", "false", 403)]
    [InlineData("PublicComposerEnabled", "yes", 403)]
    [InlineData("EnableAiComposer", "false", 503)]
    [InlineData("AiFoundryEndpoint", null, 503)]
    [InlineData("AiFoundryEndpoint", "http://example.com", 503)]
    [InlineData("AiFoundryDeploymentName", "", 503)]
    [InlineData("PublicComposer:DailyRequestLimit", null, 503)]
    [InlineData("PublicComposer:DailyRequestLimit", "101", 503)]
    [InlineData("PublicComposer:DailyTokenLimit", "2500001", 503)]
    [InlineData("PublicComposer:DailyTokenLimit", "1", 503)]
    [InlineData("PublicComposer:MaxConcurrentRequests", "3", 503)]
    [InlineData("PublicComposer:MaxGoalCharacters", "1001", 503)]
    [InlineData("PublicComposer:MaxContextBytes", "16001", 503)]
    [InlineData("PublicComposer:MaxOutputTokens", "2049", 503)]
    [InlineData("PublicComposer:ModelTimeoutSeconds", "31", 503)]
    [InlineData("PublicComposer:ModelTimeoutSeconds", "-1", 503)]
    public async Task MissingOptInOrInvalidConfig_NeverCallsModel(string key, string? value, int status)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var model = new Model();
        Assert.Equal(status, Assert.IsType<ObjectResult>(
            await Function(db, model, Config(key, value)).Compose(Request(), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        Assert.Empty(db.PublicComposerReservations);
        Assert.Empty(db.Users);
    }

    [Theory]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, true)]
    [InlineData(2_500_000, true)]
    [InlineData(2_500_001, false)]
    [InlineData(int.MaxValue, false)]
    public void Settings_DailyBudgetBoundary_PreservesEveryPerRequestGuard(int tokens, bool ready)
    {
        var config = Config("PublicComposer:DailyTokenLimit", tokens.ToString());
        config["PublicComposer:DailyRequestLimit"] = "100";
        var settings = PublicComposerSettings.Read(config);
        var baseline = PublicComposerSettings.Read(Config());
        Assert.Equal(ready, settings.Ready);
        Assert.Equal(100, settings.DailyRequestLimit);
        Assert.Equal(ready ? tokens : 0, settings.DailyTokenLimit);
        Assert.Equal(baseline.OptedIn, settings.OptedIn);
        Assert.Equal(baseline.MaxConcurrentRequests, settings.MaxConcurrentRequests);
        Assert.Equal(baseline.MaxGoalCharacters, settings.MaxGoalCharacters);
        Assert.Equal(baseline.MaxContextBytes, settings.MaxContextBytes);
        Assert.Equal(baseline.MaxOutputTokens, settings.MaxOutputTokens);
        Assert.Equal(baseline.ModelTimeoutSeconds, settings.ModelTimeoutSeconds);
        Assert.Equal(baseline.ReservedTokens, settings.ReservedTokens);
        Assert.Equal(10, baseline.DailyRequestLimit);
        Assert.Equal(100_000, baseline.DailyTokenLimit);
    }

    [Theory]
    [InlineData("""{"goal":"evidence","catalog":[]}""", "public-techniques", 400)]
    [InlineData("""{"goal":"evidence","taskIds":["private"]}""", "public-techniques", 400)]
    [InlineData("""{"goal":"evidence","collectionId":"curated-tools"}""", "public-techniques", 400)]
    [InlineData("""{"goal":"evidence"}""", "unknown", 400)]
    [InlineData("""{"goal":"evidence"}""", "public-techniques&collectionId=curated-tools", 400)]
    [InlineData("""{"goal":"   "}""", "public-techniques", 400)]
    [InlineData("{broken", "public-techniques", 400)]
    public async Task InvalidInput_NeverCallsModel(string body, string collection, int status)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var model = new Model();
        Assert.Equal(status, Assert.IsType<ObjectResult>(
            await Function(db, model).Compose(Request(body, collection), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        Assert.Empty(db.PublicComposerReservations);
    }

    [Fact]
    public async Task OversizedBodyAndGoal_NeverCallModel()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var model = new Model();
        foreach (var (body, status) in new[]
        {
            (new string(' ', 8193), 413),
            (JsonSerializer.Serialize(new { goal = new string('x', 1001) }), 400),
        })
            Assert.Equal(status, Assert.IsType<ObjectResult>(
                await Function(db, model).Compose(Request(body), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        Assert.Empty(db.PublicComposerReservations);
    }

    [Fact]
    public async Task PrivateAndWrongCollectionRecords_NeverReachModel()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db, published: false);
        db.CollectionId = "curated-tools";
        await Seed(db);
        var model = new Model();
        Assert.Equal(422, Assert.IsType<ObjectResult>(
            await Function(db, model).Compose(Request(), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        Assert.Empty(db.PublicComposerReservations);
    }

    [Fact]
    public async Task RetrievalRanksWholeCorpus_BoundsExactUtf8Context_AndNeverCreatesIdentityOrHistory()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        for (var index = 0; index < 225; index++)
        {
            var task = await Seed(db, index == 224 ? "Summarize zebra evidence" : "Generic example");
            task.Description = index == 224 ? "zebra investigation evidence" : new string('x', 1800);
            task.Tags = index == 224 ? ["zebra"] : [];
        }
        await db.SaveChangesAsync();
        var expected = await db.Tasks.SingleAsync(t => t.Name == "Summarize zebra evidence");
        var model = new Model();
        model.BeforeCall = async () =>
        {
            Assert.Null(db.Database.CurrentTransaction);
            var charged = Assert.Single(await db.PublicComposerReservations.AsNoTracking().ToListAsync());
            Assert.False(charged.Completed);
            Assert.Equal(PublicComposerSettings.Read(Config()).ReservedTokens, charged.ReservedTokens);
        };
        var response = Assert.IsType<OkObjectResult>(await Function(db, model).Compose(
            Request("""{"goal":"zebra investigation"}"""), default));
        Assert.Equal(expected.Id, Assert.Single(Assert.IsType<ComposeResponseDto>(response.Value).Steps).TaskId);
        Assert.Equal(expected.Id, Assert.Single(model.Catalog!).Id);
        Assert.True(Encoding.UTF8.GetByteCount(AzureAiComposerService.CatalogJson(model.Catalog!)) <= 12000);
        Assert.True(Assert.Single(await db.PublicComposerReservations.AsNoTracking().ToListAsync()).Completed);
        Assert.Single(db.Users);
        Assert.Empty(db.AiComposerRequestLogs);
        Assert.Empty(db.Workflows);
    }

    [Fact]
    public async Task QuotaAndUnavailableStore_BlockBeforeModel()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db);
        var settings = PublicComposerSettings.Read(Config("PublicComposer:DailyRequestLimit", "1"));
        Assert.True((await Budget(db, settings).CheckAsync(true, default)).Allowed);
        var model = new Model();
        Assert.Equal(429, Assert.IsType<ObjectResult>(await Function(db, model,
            Config("PublicComposer:DailyRequestLimit", "1")).Compose(Request(), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE PublicComposerReservations");
        Assert.Equal(503, Assert.IsType<ObjectResult>(await Function(db, model).Compose(Request(), default)).StatusCode);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task UnknownCommitResult_NeverCallsOrRetriesModel_AndKeepsCommittedCharge()
    {
        var interceptor = new UncertainCommit();
        await using var store = await RelationalTestDb.Create(interceptor);
        await using var db = store.Context();
        await Seed(db);
        interceptor.Enabled = true;
        var model = new Model();
        Assert.Equal(503, Assert.IsType<ObjectResult>(await Function(db, model).Compose(Request(), default)).StatusCode);
        Assert.Equal(0, model.Calls);
        Assert.Single(await db.PublicComposerReservations.AsNoTracking().ToListAsync());
        Assert.Equal(1, interceptor.Failures);
    }

    [Fact]
    public async Task Deadline_CancelsSingleAttemptAndConservativelyRetainsCharge()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db);
        var model = new Model { WaitForCancellation = true };
        Assert.Equal(502, Assert.IsType<ObjectResult>(await Function(db, model,
            Config("PublicComposer:ModelTimeoutSeconds", "1")).Compose(Request(), default)).StatusCode);
        Assert.Equal(1, model.Calls);
        Assert.False(Assert.Single(db.PublicComposerReservations).Completed);
    }

    [Fact]
    public async Task FabricatedReferencesAndWithdrawnItems_AreRejectedAndNeverRefunded()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var task = await Seed(db);
        var model = new Model { FabricateId = true };
        Assert.Equal(502, Assert.IsType<ObjectResult>(await Function(db, model).Compose(Request(), default)).StatusCode);
        var row = Assert.Single(db.PublicComposerReservations);
        Assert.False(row.Completed);
        // Expire only the lease; the failed request still consumes its daily reservation.
        row.LeaseExpiresAtUnixSeconds = 0;
        await db.SaveChangesAsync();
        model.FabricateId = false;
        model.BeforeCall = async () => { task.IsPublished = false; await db.SaveChangesAsync(); };
        Assert.Equal(502, Assert.IsType<ObjectResult>(await Function(db, model).Compose(Request(), default)).StatusCode);
        Assert.Equal(2, model.Calls);
        Assert.All(await db.PublicComposerReservations.ToListAsync(), r => Assert.False(r.Completed));
    }

    [Fact]
    public async Task FailedInference_RetainsLeaseAndCharge_NoAutomaticModelRetry()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db);
        var model = new Model { Fail = true };
        var function = Function(db, model);
        Assert.Equal(502, Assert.IsType<ObjectResult>(await function.Compose(Request(), default)).StatusCode);
        var request = Request();
        Assert.Equal(429, Assert.IsType<ObjectResult>(await function.Compose(request, default)).StatusCode);
        Assert.Equal(1, model.Calls);
        Assert.False(Assert.Single(db.PublicComposerReservations).Completed);
        Assert.NotEmpty(request.HttpContext.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawingAnyEvidence_RejectsEvenUnreferencedAndGapsOnlyResponses(bool gapsOnly)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Seed(db, "Summarize evidence one");
        await Seed(db, "Summarize evidence two");
        var model = new Model { GapsOnly = gapsOnly };
        model.BeforeCall = async () =>
        {
            var unreferenced = model.Catalog![1];
            var item = await db.Tasks.SingleAsync(t => t.Id == unreferenced.Id);
            item.IsPublished = false;
            await db.SaveChangesAsync();
        };
        Assert.Equal(502, Assert.IsType<ObjectResult>(await Function(db, model).Compose(Request(), default)).StatusCode);
        Assert.Equal(1, model.Calls);
        Assert.Equal(2, model.Catalog!.Count);
        Assert.False(Assert.Single(db.PublicComposerReservations).Completed);
    }

    [Fact]
    public async Task SessionReflectsConfigurationStoreAndQuota_NotAnAnonymousIdentity()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var settings = PublicComposerSettings.Read(Config("PublicComposer:DailyRequestLimit", "1"));
        var budget = Budget(db, settings);
        var session = new GetSession(new(true), settings, budget);
        async Task<string> State()
        {
            var json = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await session.Run(Request())).Value);
            Assert.False(json.GetProperty("isAuthenticated").GetBoolean());
            Assert.False(json.GetProperty("isAuthorized").GetBoolean());
            Assert.Equal(json.GetProperty("composerState").GetString() == "ready", json.GetProperty("composerEnabled").GetBoolean());
            return json.GetProperty("composerState").GetString()!;
        }
        Assert.Equal("ready", await State());
        Assert.Empty(db.PublicComposerReservations);
        await budget.CheckAsync(true, default);
        Assert.Equal("quota_exhausted", await State());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE PublicComposerReservations");
        Assert.Equal("unavailable", await State());
    }

    internal sealed class Model : IAiComposerService
    {
        public int Calls { get; private set; }
        public IReadOnlyList<CatalogTaskSnapshot>? Catalog { get; private set; }
        public Func<Task>? BeforeCall { get; set; }
        public bool Fail { get; set; }
        public bool FabricateId { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool GapsOnly { get; set; }
        public async Task<ComposeResponseDto> ComposeAsync(string goal, IReadOnlyList<CatalogTaskSnapshot> catalog, CancellationToken cancellationToken = default)
        {
            Calls++;
            Catalog = catalog;
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (BeforeCall is not null) await BeforeCall();
            if (Fail) throw new AiComposerUnavailableException("Controlled test failure");
            var task = catalog[0];
            return new("Proposal", "Problem", "Outcome",
                GapsOnly ? [] : [new(1, FabricateId ? Guid.NewGuid() : task.Id, task.Name, task.TaskType, task.TargetIds[0], null)],
                GapsOnly ? [new("No covered steps", null, null, null)] : []);
        }
    }
    private sealed class NeverCurrentUser : ICurrentUserService
    {
        public Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Anonymous Composer must not resolve an identity.");
    }
    private sealed class NeverEntra : IEntraTokenValidator
    {
        public Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Public Composer must not initialize Entra.");
    }
    private sealed class UncertainCommit : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public int Failures { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Failures++;
                throw new InvalidOperationException("Commit acknowledgement lost after database commit.");
            }
            return Task.CompletedTask;
        }
    }
}
