using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class McpCollectionScopeTests
{
    [Fact]
    public async Task DiscoveryAndEveryTool_RequireExplicitValidatedCollection_WithNoEndpointFallback()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var host = McpTestHost.Create(db, user, writesEnabled: true);
        Assert.False(McpTestHost.Request("").QueryString.HasValue);
        var discovery = await host.SendAsync("tools/list");
        var tools = Assert.IsType<JsonArray>(discovery!["result"]!["tools"]);
        Assert.Equal(20, tools.Count);
        foreach (var tool in tools)
        {
            var schema = tool!["inputSchema"]!;
            Assert.Contains("collectionId", schema["required"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.Equal(new[] { "public-techniques", "curated-tools", "playground" },
                schema["properties"]!["collectionId"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()));
            foreach (var arguments in new[] { new JsonObject(), new JsonObject { ["collectionId"] = "unknown" } })
            {
                var body = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = 1, method = "tools/call",
                    @params = new { name = tool["name"]!.GetValue<string>(), arguments }
                });
                var request = McpTestHost.Request(body, user);
                request.QueryString = new("?collectionId=public-techniques");
                var response = McpTestJson.ReadBody(await host.Endpoint.Post(request, Ct));
                Assert.Equal(-32602, response!["error"]!["code"]!.GetValue<int>());
                Assert.Contains("collectionId", response["error"]!["message"]!.GetValue<string>());
            }
        }
        Assert.Empty(db.Tasks);
        Assert.Empty(db.Workflows);
        Assert.Empty(db.AiMethods);
        Assert.Empty(db.ReportLinks);
    }

    [Fact]
    public void ActualFactory_RejectsMissingScope_AndPreservesExplicitScopeForEveryDelegationShape()
    {
        var origin = McpTestHost.Request("");
        Assert.Throws<McpArgumentException>(() => GalleryRequestFactory.Create(origin));
        Assert.Throws<McpArgumentException>(() => GalleryRequestFactory.CreateForm(origin, "POST", new Dictionary<string, string?>()));
        Assert.Throws<McpArgumentException>(() => GalleryRequestFactory.CreateJson(origin, "POST", new JsonObject()));
        Assert.Throws<McpArgumentException>(() => GalleryRequestFactory.CreateBodyless(origin, "DELETE"));
        Assert.Throws<McpArgumentException>(() => GalleryRequestFactory.ForCollection(origin, "unknown"));
        origin.QueryString = new("?collectionId=public-techniques");
        origin.Headers["x-ms-original-url"] = "https://gallery.example/api/mcp";
        var scoped = GalleryRequestFactory.ForCollection(origin, "curated-tools");
        Assert.Equal("public-techniques", origin.Query["collectionId"].ToString());
        Assert.Equal("https://gallery.example/tasks/11111111-1111-4111-8111-111111111111?collectionId=curated-tools",
            new GalleryLinkBuilder().BuildItemUrl(scoped, "tasks", Guid.Parse("11111111-1111-4111-8111-111111111111")));
        foreach (var request in new[]
        {
            GalleryRequestFactory.Create(scoped),
            GalleryRequestFactory.CreateForm(scoped, "POST", new Dictionary<string, string?>()),
            GalleryRequestFactory.CreateJson(scoped, "PUT", new JsonObject()),
            GalleryRequestFactory.CreateBodyless(scoped, "DELETE", new Dictionary<string, string?> { ["confirm"] = "true" })
        })
            Assert.Equal("curated-tools", request.Query["collectionId"].ToString());
    }

    [Fact]
    public async Task ActualEndpointReadTools_UseArgumentScopeForHandlersAndLinks_NotOuterQuery()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var publicTask = McpFunctionTests.SeedTask(db, user, "Public prompt");
        var publicMethod = Method(user, "Public method");
        var publicWorkflow = McpFunctionTests.SeedWorkflow(db, user, "Public workflow", publicTask, publicMethod);
        var publicReport = Report(user, "Public report");
        db.AiMethods.Add(publicMethod);
        db.ReportLinks.Add(publicReport);
        await db.SaveChangesAsync();
        db.CollectionId = "curated-tools";
        var curatedTask = McpFunctionTests.SeedTask(db, user, "Curated prompt");
        var curatedMethod = Method(user, "Curated method");
        var curatedWorkflow = McpFunctionTests.SeedWorkflow(db, user, "Curated workflow", curatedTask, curatedMethod);
        var curatedReport = Report(user, "Curated report");
        db.AiMethods.Add(curatedMethod);
        db.ReportLinks.Add(curatedReport);
        await db.SaveChangesAsync();
        var host = McpTestHost.Create(db, user, writesEnabled: true);

        async Task<JsonObject> Call(string name, JsonObject arguments)
        {
            arguments["collectionId"] = "curated-tools";
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments } });
            var request = McpTestHost.Request(body, user);
            request.QueryString = new("?collectionId=public-techniques");
            var response = McpTestJson.ReadBody(await host.Endpoint.Post(request, Ct));
            Assert.Null(response!["error"]);
            return response["result"]!.AsObject();
        }

        foreach (var (kind, publicId, curatedId) in new[]
        {
            ("task", publicTask.Id, curatedTask.Id), ("workflow", publicWorkflow.Id, curatedWorkflow.Id),
            ("method", publicMethod.Id, curatedMethod.Id), ("report", publicReport.Id, curatedReport.Id)
        })
        {
            var list = McpTestJson.Payload(await Call($"search_{kind}s", new JsonObject()));
            var item = Assert.Single(list["items"]!.AsArray())!;
            Assert.Equal(curatedId.ToString(), item["id"]!.GetValue<string>());
            Assert.EndsWith("?collectionId=curated-tools", item["galleryUrl"]!.GetValue<string>());
            var detail = McpTestJson.Payload(await Call($"get_{kind}", new JsonObject { ["id"] = curatedId.ToString() }));
            Assert.Equal(curatedId.ToString(), detail["id"]!.GetValue<string>());
            Assert.EndsWith("?collectionId=curated-tools", detail["galleryUrl"]!.GetValue<string>());
            Assert.True((await Call($"get_{kind}", new JsonObject { ["id"] = publicId.ToString() }))["isError"]!.GetValue<bool>());
        }
        foreach (var tool in new[] { "update_task", "delete_task" })
        {
            var arguments = new JsonObject { ["id"] = publicTask.Id.ToString() };
            if (tool == "delete_task") arguments["confirm"] = true;
            Assert.True((await Call(tool, arguments))["isError"]!.GetValue<bool>());
        }
        var crossLink = await Call("create_task", new JsonObject
        {
            ["name"] = "Invalid cross link", ["description"] = "Description", ["taskType"] = "prompt",
            ["targetIds"] = new JsonArray("microsoft.copilot.m365"), ["inlineContent"] = "Content",
            ["methodId"] = publicMethod.Id.ToString()
        });
        Assert.True(crossLink["isError"]!.GetValue<bool>());
        Assert.DoesNotContain("Public method", crossLink.ToJsonString());
        Assert.Equal(2, await db.Tasks.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task WorkflowTaskTypeFilter_HasActualRestAndMcpParity_AndRejectsUnsupportedKinds()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var prompt = McpFunctionTests.SeedTask(db, user, "Prompt");
        var agent = McpFunctionTests.SeedTask(db, user, "Agent");
        agent.TaskType = "agent";
        McpFunctionTests.SeedWorkflow(db, user, "Prompt workflow", prompt);
        var expected = McpFunctionTests.SeedWorkflow(db, user, "Agent workflow", agent);
        await db.SaveChangesAsync();
        var api = new Workflows(db, new FakeCurrentUserService(user));
        var page = Assert.IsType<PagedResultDto<WorkflowSummaryDto>>(Assert.IsType<OkObjectResult>(
            await api.List(Query(("taskType", "agent")), Ct)).Value);
        Assert.Equal(expected.Id, Assert.Single(page.Items).Id);
        Assert.IsType<BadRequestObjectResult>(await api.List(Query(("taskType", "unknown")), Ct));
        var host = McpTestHost.Create(db, user);
        var actual = McpTestJson.Payload(await host.CallToolAsync("search_workflows", "public-techniques", new { taskType = "agent" }, user));
        Assert.Equal(expected.Id.ToString(), Assert.Single(actual["items"]!.AsArray())!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task FormatWrite_OmissionPreserves_BlankClears_AndUnknownFormatDoesNotMutate()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Prompt");
        task.Format = "text.plain";
        await db.SaveChangesAsync();
        var host = McpTestHost.Create(db, user, writesEnabled: true);
        var preserved = McpTestJson.Payload(await host.CallToolAsync("update_task", "public-techniques",
            new { id = task.Id.ToString(), description = "Narrow edit" }, user));
        Assert.Equal("text.plain", preserved["format"]!.GetValue<string>());
        var invalid = await host.SendAsync("tools/call", new
        {
            name = "update_task", arguments = new { collectionId = "public-techniques", id = task.Id.ToString(), format = "unknown" }
        }, signedInAs: user);
        Assert.Equal(-32602, invalid!["error"]!["code"]!.GetValue<int>());
        Assert.Equal("text.plain", (await db.Tasks.SingleAsync()).Format);
        var cleared = McpTestJson.Payload(await host.CallToolAsync("update_task", "public-techniques",
            new { id = task.Id.ToString(), format = "" }, user));
        Assert.Null(cleared["format"]);
        Assert.Null((await db.Tasks.AsNoTracking().SingleAsync()).Format);
        var definition = host.Catalog.Find("update_task")!.InputSchema["properties"]!["format"]!;
        Assert.Contains("", definition["enum"]!.AsArray().Where(x => x is not null).Select(x => x!.GetValue<string>()));
    }

    private static ReportLink Report(User user, string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, Description = "Report", Url = "https://example.com/report",
        Kind = ReportLinkKind.Report, CreatedByUserId = user.Id, LastEditedByUserId = user.Id, Version = Guid.NewGuid()
    };
}
