using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class PublicMcpTests
{
    private static IConfiguration Config(bool master = true, bool optedIn = true, bool demo = true, bool writes = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicDemoEnabled"] = demo.ToString(), ["EnableMcpServer"] = master.ToString(),
            ["EnablePublicMcp"] = optedIn.ToString(), ["EnableMcpWrites"] = writes.ToString(),
            ["PublicMcpAllowedOrigins"] = "https://gallery.example",
        }).Build();

    [Fact]
    public async Task Boundary_AllSwitchCombinationsAndExactTransportOnly_KeepAnonymousAndNeverValidateEntra()
    {
        foreach (var master in new[] { false, true })
        foreach (var optedIn in new[] { false, true })
        foreach (var writes in new[] { false, true })
        {
            var configuration = Config(master, optedIn, writes: writes);
            var options = McpOptions.FromConfiguration(configuration);
            Assert.False(options.WritesEnabled);
            foreach (var (method, path) in new[]
            {
                ("POST", "/api/mcp"), ("GET", "/api/mcp"), ("DELETE", "/api/mcp"),
                ("PUT", "/api/mcp"), ("POST", "/api/mcp/"), ("POST", "/api/mcp/other"),
                ("POST", "/api/tasks"), ("POST", "/api/feedback"), ("POST", "/api/ai/compose")
            })
            {
                var context = new DefaultHttpContext();
                context.Response.Body = new MemoryStream();
                context.Request.Method = method;
                context.Request.Path = path;
                context.Request.QueryString = new("?collectionId=public-techniques");
                Principal(context.Request, User());
                context.Request.Headers["x-ms-client-principal"] = "forged";
                var boundary = new GalleryRequestAuthorization(configuration, new NeverValidate(),
                    NullLogger<GalleryRequestAuthorization>.Instance);
                Assert.Equal(master && optedIn && McpOptions.IsTransportRequest(context.Request),
                    await boundary.AuthorizeAsync(context, Ct));
                Assert.False(context.User.Identity!.IsAuthenticated);
                Assert.True(PublicDemoPolicy.For(context.Request).Enabled);
            }
        }
        Assert.False(McpOptions.FromConfiguration(new ConfigurationBuilder().Build()).PublicEnabled);
        Assert.True(McpOptions.FromConfiguration(Config(demo: false)).WritesEnabled);
        foreach (var (key, value) in new[]
        {
            ("EnablePublicMcp", "yes"), ("EnableMcpServer", ""), ("EnableMcpWrites", "1"),
            ("PublicMcpAllowedOrigins", "http://gallery.example"), ("PublicMcpAllowedOrigins", "https://gallery.example/"),
            ("PublicMcpAllowedOrigins", "https://gallery.example/path"), ("PublicMcpAllowedOrigins", "null"),
            ("PublicMcpAllowedOrigins", "https://user:password@gallery.example")
        })
        {
            var malformed = new ConfigurationBuilder().AddConfiguration(Config())
                .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
            Assert.Throws<InvalidOperationException>(() => McpOptions.FromConfiguration(malformed));
        }
    }

    [Fact]
    public async Task HttpThroughRealToolsAndHandlers_OnlyPublishedScopedData_NoIdentityOrWrites()
    {
        var save = new SaveHook();
        await using var store = await RelationalTestDb.Create(save);
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var rows = new Dictionary<string, (Guid Task, Guid Workflow, Guid Method, Guid Report, Guid HiddenTask)>();
        foreach (var collection in new[] { "public-techniques", "curated-tools" })
        {
            db.CollectionId = collection;
            var method = Method(user, $"Published {collection} method"); method.IsPublished = true;
            var hiddenMethod = Method(user, $"SECRET {collection} method");
            db.AiMethods.AddRange(method, hiddenMethod);
            var task = McpFunctionTests.SeedTask(db, user, $"Published {collection} task"); task.IsPublished = true;
            task.MethodId = hiddenMethod.Id;
            var hiddenTask = McpFunctionTests.SeedTask(db, user, $"SECRET {collection} task");
            var workflow = McpFunctionTests.SeedWorkflow(db, user, $"Published {collection} workflow", task, hiddenMethod);
            workflow.IsPublished = true;
            workflow.Steps.Add(new WorkflowStep
            {
                Id = Guid.NewGuid(), WorkflowId = workflow.Id, TaskId = hiddenTask.Id, SequenceOrder = 3,
                TargetId = "microsoft.copilot.m365", TaskNameSnapshot = hiddenTask.Name, TaskTypeSnapshot = "prompt"
            });
            McpFunctionTests.SeedWorkflow(db, user, $"SECRET {collection} workflow", hiddenTask);
            var report = Report(user, $"Published {collection} report", true);
            db.ReportLinks.AddRange(report, Report(user, $"SECRET {collection} report", false));
            await db.SaveChangesAsync();
            rows.Add(collection, (task.Id, workflow.Id, method.Id, report.Id, hiddenTask.Id));
        }
        db.ChangeTracker.Clear();
        save.BeforeSave = _ => throw new InvalidOperationException("No public MCP operation may save.");
        using var server = Server(db);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("x-ms-client-principal", "forged");
        client.DefaultRequestHeaders.Add("x-ms-client-principal-id", user.EntraObjectId);
        client.DefaultRequestHeaders.Add("x-ms-token-aad-access-token", "forged");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "forged");
        var initialize = await Rpc(client, "initialize");
        Assert.Contains("read-only", initialize["result"]!["instructions"]!.GetValue<string>());
        var discovery = await Rpc(client, "tools/list");
        var definitions = discovery["result"]!["tools"]!.AsArray();
        Assert.Equal(8, definitions.Count);
        Assert.All(definitions, tool =>
        {
            Assert.True(McpToolCatalog.IsPublicRead(tool!["name"]!.GetValue<string>()));
            Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
            if (tool["inputSchema"]!["properties"]!["pageSize"] is { } size)
                Assert.Equal(25, size["maximum"]!.GetValue<int>());
        });
        Assert.Empty((await Rpc(client, "ping"))["result"]!.AsObject());
        using var notification = await client.PostAsync("/api/mcp?collectionId=public-techniques",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);
        Assert.Equal("", await notification.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.GetAsync("/api/mcp?collectionId=public-techniques")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync("/api/mcp?collectionId=public-techniques")).StatusCode);
        foreach (var (collection, ids) in rows)
        {
            foreach (var (kind, id) in new[] { ("task", ids.Task), ("workflow", ids.Workflow), ("method", ids.Method), ("report", ids.Report) })
            {
                var list = await Call(client, $"search_{kind}s", new JsonObject { ["collectionId"] = collection, ["pageSize"] = 25 });
                var payload = list["result"]!["structuredContent"]!;
                Assert.Equal(id.ToString(), Assert.Single(payload["items"]!.AsArray())!["id"]!.GetValue<string>());
                Assert.Equal(25, payload["pageSize"]!.GetValue<int>());
                var detail = await Call(client, $"get_{kind}", new JsonObject { ["collectionId"] = collection, ["id"] = id.ToString() });
                Assert.DoesNotContain("SECRET", detail.ToJsonString());
                Assert.EndsWith($"?collectionId={collection}", detail["result"]!["structuredContent"]!["galleryUrl"]!.GetValue<string>());
                if (kind is "task" or "workflow") Assert.Null(detail["result"]!["structuredContent"]!["methodId"]);
                if (kind == "workflow")
                {
                    var steps = detail["result"]!["structuredContent"]!["steps"]!.AsArray();
                    Assert.Equal("Review before sending", steps.Single(x => x!["sequenceOrder"]!.GetValue<int>() == 2)!["taskName"]!.GetValue<string>());
                    var unavailable = steps.Where(x => x!["sequenceOrder"]!.GetValue<int>() == 3).ToArray();
                    Assert.NotEmpty(unavailable);
                    Assert.All(unavailable, step =>
                    {
                        Assert.Null(step!["taskId"]); Assert.Null(step["targetId"]); Assert.Null(step["taskType"]);
                        Assert.Equal("Unavailable task", step["taskName"]!.GetValue<string>());
                    });
                }
                var otherCollection = collection == "public-techniques" ? "curated-tools" : "public-techniques";
                var hidden = await Call(client, $"get_{kind}", new JsonObject { ["collectionId"] = otherCollection, ["id"] = id.ToString() });
                Assert.True(hidden["result"]!["isError"]!.GetValue<bool>());
            }
            var draft = await Call(client, "get_task", new JsonObject { ["collectionId"] = collection, ["id"] = ids.HiddenTask.ToString() });
            Assert.True(draft["result"]!["isError"]!.GetValue<bool>());
        }
        foreach (var tool in new McpToolCatalog(new McpOptions { WritesEnabled = true }).All.Where(x => x.RequiresWrite))
            Assert.Equal(-32602, (await Call(client, tool.Name, new JsonObject { ["collectionId"] = "public-techniques" }))["error"]!["code"]!.GetValue<int>());
        foreach (var args in new[]
        {
            new JsonObject(), new JsonObject { ["collectionId"] = "unknown" },
            new JsonObject { ["collectionId"] = new JsonArray("public-techniques", "curated-tools") },
            new JsonObject { ["collectionId"] = "public-techniques", ["pageSize"] = 26 }
        })
            Assert.Equal(-32602, (await Call(client, "search_tasks", args))["error"]!["code"]!.GetValue<int>());
        using var duplicate = await client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_tasks","arguments":{"collectionId":"public-techniques","collectionId":"curated-tools"}}}"""));
        Assert.Equal(-32600, JsonNode.Parse(await duplicate.Content.ReadAsStringAsync())!["error"]!["code"]!.GetValue<int>());
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(4, await db.Tasks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, await db.Workflows.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, await db.AiMethods.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, await db.ReportLinks.IgnoreQueryFilters().CountAsync());
        Assert.Empty(db.Comments); Assert.Empty(db.Ratings);
        Assert.Empty(db.AiComposerRequestLogs); Assert.Empty(db.PublicComposerReservations);
    }

    [Fact]
    public async Task OriginValidationAndStreamingRequestBytes_AreEnforcedBeforeTools()
    {
        await using var db = TestDbContextFactory.Create();
        using var server = Server(db);
        using var client = server.CreateClient();
        foreach (var origin in new[] { "null", "https://evil.example", "https://gallery.example/", "http://gallery.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp?collectionId=public-techniques") { Content = new StringContent(Envelope("ping")) };
            request.Headers.Add("Origin", origin);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        }
        using var approved = new HttpRequestMessage(HttpMethod.Post, "/api/mcp?collectionId=public-techniques") { Content = new StringContent(Envelope("ping")) };
        approved.Headers.Add("Origin", "https://gallery.example");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(approved)).StatusCode);
        using var multiple = new HttpRequestMessage(HttpMethod.Post, "/api/mcp?collectionId=public-techniques") { Content = new StringContent(Envelope("ping")) };
        multiple.Headers.Add("Origin", new[] { "https://gallery.example", "https://evil.example" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(multiple)).StatusCode);
        foreach (var stream in new[] { false, true })
        {
            var body = Encoding.UTF8.GetBytes(new string('x', PublicMcpGuard.MaxRequestBytes + 1));
            using HttpContent content = stream ? new UnknownLengthContent(body) : new ByteArrayContent(body);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/api/mcp?collectionId=public-techniques", content)).StatusCode);
        }
        var exact = Envelope("ping").PadRight(PublicMcpGuard.MaxRequestBytes);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/mcp?collectionId=public-techniques", new UnknownLengthContent(Encoding.UTF8.GetBytes(exact)))).StatusCode);
    }

    [Fact]
    public async Task ActualSearchHandlers_EnforceTwentyFiveItemsAndAllowExplicitNextPage()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        for (var n = 0; n < 28; n++)
        {
            var task = McpFunctionTests.SeedTask(db, user, $"Task {n}"); task.IsPublished = true;
            var method = Method(user, $"Method {n}"); method.IsPublished = true; db.AiMethods.Add(method);
            var workflow = McpFunctionTests.SeedWorkflow(db, user, $"Workflow {n}", task); workflow.IsPublished = true;
            db.ReportLinks.Add(Report(user, $"Report {n}", true));
        }
        await db.SaveChangesAsync();
        using var server = Server(db);
        using var client = server.CreateClient();
        foreach (var name in new[] { "search_tasks", "search_workflows", "search_methods", "search_reports" })
        {
            var first = await Call(client, name, new JsonObject { ["collectionId"] = "public-techniques" });
            Assert.Equal(25, first["result"]!["structuredContent"]!["items"]!.AsArray().Count);
            var next = await Call(client, name, new JsonObject { ["collectionId"] = "public-techniques", ["page"] = 2 });
            Assert.Equal(3, next["result"]!["structuredContent"]!["items"]!.AsArray().Count);
            foreach (var size in new JsonNode?[] { JsonValue.Create(0), JsonValue.Create(-1), JsonValue.Create(26), JsonValue.Create(100), JsonValue.Create("25"), null })
                Assert.Equal(-32602, (await Call(client, name, new JsonObject { ["collectionId"] = "public-techniques", ["pageSize"] = size }))["error"]!["code"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task SyntheticRequests_PreservePublicStampWithoutDi_AndRejectDirectForgedWrites()
    {
        await using var db = TestDbContextFactory.Create();
        var original = Request("");
        Principal(original, User());
        var scoped = GalleryRequestFactory.ForCollection(original, "curated-tools");
        Assert.True(PublicDemoPolicy.For(scoped).Enabled);
        Assert.False(scoped.HttpContext.User.Identity!.IsAuthenticated);
        var delegated = GalleryRequestFactory.Create(scoped);
        Assert.True(PublicDemoPolicy.For(delegated).Enabled);
        Assert.False(delegated.HttpContext.User.Identity!.IsAuthenticated);
        var privateOptions = new McpOptions { WritesEnabled = true };
        var catalog = new McpToolCatalog(privateOptions);
        Assert.Equal(20, catalog.Visible.Count);
        Assert.Equal(8, catalog.VisibleFor(true).Count);
        Assert.Equal(20, catalog.Visible.Count);
        var tools = McpTestHost.Create(db, writesEnabled: true).Tools;
        foreach (var name in catalog.All.Where(x => x.RequiresWrite).Select(x => x.Name))
            await Assert.ThrowsAsync<McpArgumentException>(() =>
                tools.ExecuteAsync(name, new JsonObject { ["collectionId"] = "public-techniques" }, scoped, Ct));
        Assert.Empty(db.Users);
        Assert.Empty(db.Tasks);
    }

    [Fact]
    public async Task CompleteSerializedResponseBound_CountsBothPayloadRepresentationsAndUtf8()
    {
        var characters = 0;
        var tools = new StubTools((_, _) => Task.FromResult(McpToolResult.Ok(new JsonObject { ["value"] = new string('\u00e9', characters) })));
        var endpoint = Endpoint(tools);
        var body = Envelope("tools/call", new { name = "search_tasks", arguments = new { collectionId = "public-techniques" } });
        var empty = Assert.IsType<ContentResult>(await endpoint.Post(Request(body), Ct));
        var overhead = Encoding.UTF8.GetByteCount(empty.Content!);
        // Two UTF-8 bytes per character, repeated in text and structuredContent.
        characters = (PublicMcpGuard.MaxResponseBytes - overhead) / 4;
        var bounded = Assert.IsType<ContentResult>(await endpoint.Post(Request(body), Ct));
        Assert.InRange(Encoding.UTF8.GetByteCount(bounded.Content!), PublicMcpGuard.MaxResponseBytes - 3, PublicMcpGuard.MaxResponseBytes);
        characters++;
        var result = Assert.IsType<ObjectResult>(await endpoint.Post(Request(body), Ct));
        Assert.Equal(413, result.StatusCode);
        Assert.Contains("mcp_response_too_large", JsonSerializer.Serialize(result.Value));
        await using var db = TestDbContextFactory.Create();
        using var server = Server(db, tools);
        using var client = server.CreateClient();
        using var response = await client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(body));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("mcp_response_too_large", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task PublicAudit_DoesNotLogBodyTokensPromptsOrUntrustedClientLabels()
    {
        const string secret = "SECRET-body-token-prompt";
        var options = McpOptions.FromConfiguration(Config());
        var audit = new RecordingAuditLogger();
        var log = new CapturingLogger();
        var endpoint = new Mcp(options, new McpToolCatalog(options),
            new StubTools((_, _) => throw new InvalidOperationException(secret)),
            audit, log, new PublicDemoPolicy(true), new PublicMcpGuard(TimeProvider.System));
        await endpoint.Post(Request(Envelope(secret)), Ct);
        await endpoint.Post(Request(Envelope("tools/call", new { name = secret, clientInfo = new { name = secret } })), Ct);
        var request = Request(Envelope("tools/call", new
        {
            name = "search_tasks", arguments = new { collectionId = "public-techniques", q = secret },
            clientInfo = new { name = secret }
        }));
        request.Headers.Authorization = $"Bearer {secret}";
        await endpoint.Post(request, Ct);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(audit.Records));
        Assert.DoesNotContain(secret, string.Join(" ", log.Messages));
        Assert.All(audit.Records, record =>
        {
            Assert.Equal("Unauthenticated", record.ActingUser);
            Assert.Null(record.ActingUserObjectId); Assert.Null(record.ClientName);
        });
    }

    [Fact]
    public async Task WorkerGuard_ConcurrencyRateWindowAndCancelledOperationRelease()
    {
        var clock = new Clock();
        var guard = new PublicMcpGuard(clock);
        var first = guard.TryEnter(out _)!; var second = guard.TryEnter(out _)!;
        Assert.Null(guard.TryEnter(out var busyRetry)); Assert.Equal(1, busyRetry);
        first.Dispose(); first.Dispose(); second.Dispose();
        for (var n = 2; n < 60; n++) guard.TryEnter(out _)!.Dispose();
        Assert.Null(guard.TryEnter(out var rateRetry)); Assert.Equal(60, rateRetry);
        clock.Now += TimeSpan.FromSeconds(59);
        Assert.Null(guard.TryEnter(out rateRetry)); Assert.Equal(1, rateRetry);
        clock.Now += TimeSpan.FromSeconds(1);
        guard.TryEnter(out _)!.Dispose();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = Endpoint(new StubTools(async (_, token) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
            return McpToolResult.Error("unreachable");
        }), guard: new PublicMcpGuard(TimeProvider.System));
        using var cancelled = new CancellationTokenSource();
        var pending = endpoint.Post(Request(Envelope("tools/call", new { name = "search_tasks", arguments = new { collectionId = "public-techniques" } })), cancelled.Token);
        await started.Task;
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.IsType<ContentResult>(await endpoint.Post(Request(Envelope("ping")), Ct));
    }

    [Fact]
    public async Task HttpRateLimit_ReturnsRetryAfterAndNoFalseSuccess()
    {
        await using var db = TestDbContextFactory.Create();
        using var server = Server(db);
        using var client = server.CreateClient();
        for (var n = 0; n < 60; n++) await Rpc(client, "ping");
        using var denied = await client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(Envelope("ping")));
        Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
        Assert.InRange(denied.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        Assert.Contains("mcp_busy", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deadline_Returns504ButRetainsConcurrencyUntilUnderlyingWorkStops()
    {
        var finish = new TaskCompletionSource<McpToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var guard = new PublicMcpGuard(TimeProvider.System);
        await using var db = TestDbContextFactory.Create();
        using var server = Server(db, new StubTools((_, _) =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothStarted.TrySetResult();
            return finish.Task;
        }), guard);
        using var client = server.CreateClient();
        var call = Envelope("tools/call", new { name = "search_tasks", arguments = new { collectionId = "public-techniques" } });
        var first = client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(call));
        var second = client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(call));
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(Envelope("ping")))).StatusCode);
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await second).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/mcp?collectionId=public-techniques", new StringContent(Envelope("ping")))).StatusCode);
        finish.SetResult(McpToolResult.Ok(new JsonObject()));
        // Wait for the underlying continuations to release both leases, not just HTTP timeout.
        for (var n = 0; n < 100; n++)
        {
            if (guard.TryEnter(out _) is { } lease) { lease.Dispose(); return; }
            await Task.Delay(10);
        }
        Assert.Fail("Timed-out underlying operations did not release their leases.");
    }

    private static Mcp Endpoint(IGalleryMcpTools tools, PublicMcpGuard? guard = null)
    {
        var options = McpOptions.FromConfiguration(Config());
        return new(options, new McpToolCatalog(options), tools, new RecordingAuditLogger(),
            NullLogger<Mcp>.Instance, new PublicDemoPolicy(true), guard ?? new PublicMcpGuard(TimeProvider.System));
    }
    private static HttpRequest Request(string body)
    {
        var request = McpTestHost.Request(body);
        request.QueryString = new QueryString("?collectionId=public-techniques");
        new PublicDemoPolicy(true).Stamp(request.HttpContext);
        return request;
    }
    private static string Envelope(string method, object? parameters = null) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters });
    private static async Task<JsonObject> Rpc(HttpClient client, string method, object? parameters = null, string collectionId = "public-techniques")
    {
        using var response = await client.PostAsync($"/api/mcp?collectionId={Uri.EscapeDataString(collectionId)}",
            new StringContent(Envelope(method, parameters), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }
    private static Task<JsonObject> Call(HttpClient client, string name, JsonObject arguments)
    {
        var collectionId = arguments["collectionId"] is JsonValue selected &&
                           selected.TryGetValue<string>(out var value) &&
                           value is "public-techniques" or "curated-tools" or "playground"
            ? value
            : "public-techniques";
        return Rpc(client, "tools/call", new { name, arguments }, collectionId);
    }
    private static TestServer Server(AppDbContext db, IGalleryMcpTools? overrideTools = null, PublicMcpGuard? guard = null)
    {
        var config = Config();
        var boundary = new GalleryRequestAuthorization(config, new NeverValidate(), NullLogger<GalleryRequestAuthorization>.Instance);
        var current = new NeverCurrentUser();
        var tools = new GalleryMcpTools(new Tasks(db, current, new DisabledBlobStorageService(), new FakeFileValidationService()),
            new Workflows(db, current), new Methods(db, current), new Reports(db, current), new GalleryLinkBuilder("https://gallery.example"));
        var endpoint = Endpoint(overrideTools ?? tools, guard);
        return new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddControllers(); services.AddSingleton(db);
        }).Configure(app =>
        {
            app.Use(async (context, next) => { if (await boundary.AuthorizeAsync(context, context.RequestAborted)) await next(); });
            app.Run(async context =>
            {
                Assert.False(context.User.Identity!.IsAuthenticated);
                var result = context.Request.Method switch
                {
                    "GET" => endpoint.Get(context.Request), "DELETE" => endpoint.Delete(context.Request),
                    _ => await endpoint.Post(context.Request, context.RequestAborted)
                };
                await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
            });
        }));
    }
    private static ReportLink Report(User user, string name, bool published) => new()
    {
        Id = Guid.NewGuid(), Name = name, Description = "Directory metadata only", Url = "https://example.com/not-fetched",
        Kind = ReportLinkKind.Report, CreatedByUserId = user.Id, LastEditedByUserId = user.Id, IsPublished = published, Version = Guid.NewGuid()
    };
    private sealed class NeverValidate : IEntraTokenValidator
    {
        public Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Public MCP must not validate Entra or inherit a principal.");
    }
    private sealed class NeverCurrentUser : ICurrentUserService
    {
        public Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Public MCP must not create/resolve an anonymous contributor.");
    }
    private sealed class StubTools(Func<HttpRequest, CancellationToken, Task<McpToolResult>> execute) : IGalleryMcpTools
    {
        public Task<McpToolResult> ExecuteAsync(string toolName, JsonObject? arguments, HttpRequest originating, CancellationToken cancellationToken) =>
            execute(originating, cancellationToken);
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<Mcp>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}
