using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class PublicPlaygroundMcpTests
{
    [Fact]
    public async Task PublicConnectionsExposeEightReadToolsOrTwentyScopedPlaygroundTools()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = Host(db, writes: true);

        foreach (var (collection, expectedCount) in new[]
        {
            ("public-techniques", 8),
            ("curated-tools", 8),
            ("playground", 20),
        })
        {
            var response = await Post(host, "tools/list", null, collection);
            var tools = Assert.IsType<JsonArray>(response!["result"]!["tools"]);
            Assert.Equal(expectedCount, tools.Count);
            var names = tools.Select(tool => tool!["name"]!.GetValue<string>()).ToArray();
            Assert.Equal(collection == "playground", names.Contains("create_task"));
            foreach (var tool in tools)
            {
                var schema = tool!["inputSchema"]!;
                Assert.Contains("collectionId", schema["required"]!.AsArray()
                    .Select(value => value!.GetValue<string>()));
                Assert.Equal(new[] { collection },
                    schema["properties"]!["collectionId"]!["enum"]!.AsArray()
                        .Select(value => value!.GetValue<string>()));
            }
        }
    }

    [Fact]
    public async Task PublicPlaygroundCreateUsesSandboxActorAndRejectsCrossCollectionScope()
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
        var publishedUser = User();
        db.Users.Add(publishedUser);
        var publishedTask = McpFunctionTests.SeedTask(db, publishedUser, "Published task");
        db.CollectionId = CollectionScope.PublicTechniques;
        await db.SaveChangesAsync();
        var host = Host(db, writes: true);

        var created = await Call(host, "create_task", "playground", new JsonObject
        {
            ["name"] = "Disposable prompt",
            ["description"] = "A public sandbox example",
            ["taskType"] = "prompt",
            ["targetIds"] = new JsonArray("microsoft.copilot.m365"),
            ["inlineContent"] = "Reviewed content",
        });
        Assert.False(created["isError"]!.GetValue<bool>());
        var result = McpTestJson.Payload(created);
        Assert.Equal(actor.Id, result["createdBy"]!["id"]!.GetValue<Guid>());
        Assert.Null(actor.EntraTenantId);
        Assert.Null(actor.EntraObjectId);
        var savedPlaygroundTask = await db.Tasks.IgnoreQueryFilters()
            .SingleAsync(task => task.CollectionId == CollectionScope.Playground);
        Assert.Equal(actor.Id, savedPlaygroundTask.CreatedByUserId);
        Assert.Equal(2, await db.Tasks.IgnoreQueryFilters().CountAsync());

        var wrongScope = await Call(host, "create_task", "playground", new JsonObject
        {
            ["collectionId"] = "public-techniques",
            ["name"] = "Must not cross scope",
            ["description"] = "Invalid scope",
            ["taskType"] = "prompt",
            ["targetIds"] = new JsonArray("microsoft.copilot.m365"),
            ["inlineContent"] = "No write",
        });
        Assert.Equal(-32602, wrongScope!["error"]!["code"]!.GetValue<int>());

        var hiddenWrite = await Call(host, "create_task", "public-techniques", new JsonObject
        {
            ["name"] = "Must not write",
            ["description"] = "Published collections are immutable",
            ["taskType"] = "prompt",
            ["targetIds"] = new JsonArray("microsoft.copilot.m365"),
            ["inlineContent"] = "No write",
        });
        Assert.Equal(-32602, hiddenWrite["error"]!["code"]!.GetValue<int>());
        Assert.Equal(publishedTask.Id, (await db.Tasks.IgnoreQueryFilters()
            .SingleAsync(task => task.CollectionId == CollectionScope.PublicTechniques)).Id);
        Assert.Equal(2, await db.Tasks.IgnoreQueryFilters().CountAsync());
    }

    private static McpTestHost Host(AppDbContext db, bool writes) =>
        McpTestHost.Create(db, publicDemoPolicy: new PublicDemoPolicy(true, writes));

    private static async Task<JsonObject?> Post(McpTestHost host, string method, object? parameters, string collection)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = method,
        };
        if (parameters is not null)
            envelope["params"] = JsonSerializer.SerializeToNode(parameters);
        return McpTestJson.ReadBody(await host.Endpoint.Post(
            McpTestHost.Request(envelope.ToJsonString(), collectionId: collection), Ct));
    }

    private static async Task<JsonObject> Call(
        McpTestHost host,
        string name,
        string collection,
        JsonObject arguments)
    {
        arguments["collectionId"] ??= collection;
        var response = await Post(host, "tools/call",
            new { name, arguments = arguments.AsObject() }, collection);
        Assert.NotNull(response);
        return response!["error"] is not null
            ? response
            : response["result"]!.AsObject();
    }
}
