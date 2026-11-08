using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// Behaviour of the `/api/mcp` endpoint: the protocol surface (User Story 1) and the operator
/// controls (User Story 3).
/// </summary>
public class McpFunctionTests
{
    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task Initialize_AdvertisesTools_AndEchoesASupportedProtocolVersion(string protocolVersion)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var response = await host.SendAsync("initialize", new
        {
            protocolVersion,
            clientInfo = new { name = "probe", version = "1.0" },
            capabilities = new { },
        });

        var result = (JsonObject)response!["result"]!;
        Assert.Equal(protocolVersion, result["protocolVersion"]!.GetValue<string>());
        Assert.False(result["capabilities"]!["tools"]!["listChanged"]!.GetValue<bool>());
        Assert.Equal(McpOptions.ServerName, result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Contains("read-only", result["instructions"]!.GetValue<string>());

        // The client it reported is carried into the audit trail.
        Assert.Equal("probe", Assert.Single(host.Audit.Records).ClientName);
    }

    [Fact]
    public async Task Initialize_FallsBackToTheServerDefault_ForAnUnknownProtocolVersion()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var response = await host.SendAsync("initialize", new { protocolVersion = "1999-01-01" });

        Assert.Equal(
            McpOptions.DefaultProtocolVersion,
            response!["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsList_ReturnsTheSelfDescribingCatalog()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var response = await host.SendAsync("tools/list");
        var tools = (JsonArray)response!["result"]!["tools"]!;

        Assert.Equal(host.Catalog.Visible.Count, tools.Count);
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "search_workflows");
        Assert.All(tools, t => Assert.NotNull(t!["inputSchema"]!["properties"]));
    }

    [Fact]
    public async Task Ping_Succeeds()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();

        var response = await McpTestHost.Create(db).SendAsync("ping");

        Assert.Empty((JsonObject)response!["result"]!);
    }

    [Fact]
    public async Task SearchThenRetrieve_ReturnsTheWorkflowWithItsOrderedStepsAndLinkedMethod()
    {
        // SC-001: connect, search for a known workflow, retrieve it with steps and method.
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Author");
        var method = Method(user, "Synthesis");
        db.Users.Add(user);
        db.AiMethods.Add(method);
        var task = SeedTask(db, user, "Summarise transcript");
        var workflow = SeedWorkflow(db, user, "Customer onboarding review", task, method);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user);

        var search = await host.CallToolAsync("search_workflows", "public-techniques", new { q = "onboarding" }, user);
        Assert.False(search["isError"]!.GetValue<bool>());
        var page = (JsonObject)McpTestJson.Payload(search);
        var item = Assert.Single((JsonArray)page["items"]!)!;
        Assert.Equal(workflow.Id.ToString(), item["id"]!.GetValue<string>());
        Assert.Equal($"{McpTestHost.BaseUrl}/workflows/{workflow.Id}?collectionId=public-techniques", item["galleryUrl"]!.GetValue<string>());

        var detail = await host.CallToolAsync("get_workflow", "public-techniques", new { id = workflow.Id.ToString() }, user);
        var payload = (JsonObject)McpTestJson.Payload(detail);
        Assert.Equal("Customer onboarding review", payload["name"]!.GetValue<string>());
        Assert.Equal(method.Id.ToString(), payload["methodId"]!.GetValue<string>());

        var steps = (JsonArray)payload["steps"]!;
        Assert.Equal(2, steps.Count);
        Assert.Equal([1, 2], steps.Select(s => s!["sequenceOrder"]!.GetValue<int>()).ToArray());
        Assert.Equal("Summarise transcript", steps[0]!["taskName"]!.GetValue<string>());

        // Enums stay human-readable, exactly as the browser receives them.
        Assert.Equal("microsoft.copilot.m365", steps[0]!["targetId"]!.GetValue<string>());

        // structuredContent carries the same payload as the text block, so no client sees less.
        Assert.Equal(payload.ToJsonString(), detail["structuredContent"]!.ToJsonString());
    }

    [Fact]
    public async Task Retrieving_SomethingThatDoesNotExist_IsAToolError_NotAnEmptySuccess()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);
        var missing = Guid.NewGuid();

        foreach (var (tool, kind) in new[]
                 {
                     ("get_task", "task"),
                     ("get_workflow", "workflow"),
                     ("get_method", "AI method"),
                     ("get_report", "report or AI app"),
                 })
        {
            var result = await host.CallToolAsync(tool, "public-techniques", new { id = missing.ToString() });

            Assert.True(result["isError"]!.GetValue<bool>(), $"{tool} did not report an error.");
            Assert.Equal($"No {kind} exists with id {missing}.", McpTestJson.Text(result));
        }
    }

    [Fact]
    public async Task OversizedPageSize_IsClampedToTheExistingMaximum_NotRejected()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        db.AiMethods.Add(Method(user));
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user);

        // Methods and reports answer 400 for pageSize > 100; the MCP layer must clamp first so
        // every tool behaves the same way (spec edge case).
        foreach (var tool in new[] { "search_methods", "search_reports", "search_tasks", "search_workflows" })
        {
            var result = await host.CallToolAsync(tool, "public-techniques", new { pageSize = 5000, page = 0 });

            Assert.False(result["isError"]!.GetValue<bool>(), $"{tool} rejected an oversized page.");
            var payload = (JsonObject)McpTestJson.Payload(result);
            Assert.Equal(100, payload["pageSize"]!.GetValue<int>());
            Assert.Equal(1, payload["page"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task AbsurdPageNumber_IsClampedSoEveryToolStillAnswers()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        db.AiMethods.Add(Method(user));
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user);

        // Left unclamped, `page` diverges three ways across the delegated endpoints: methods and
        // reports 400, workflows overflows (page - 1) * pageSize into a negative SQL OFFSET, and
        // tasks page in memory where a negative Skip silently returns page 1 while still reporting
        // the requested page — an agent confidently shown the wrong rows. All four must clamp.
        foreach (var tool in new[] { "search_methods", "search_reports", "search_tasks", "search_workflows" })
        {
            var result = await host.CallToolAsync(tool, "public-techniques", new { page = int.MaxValue, pageSize = 25 });

            Assert.False(result["isError"]!.GetValue<bool>(), $"{tool} failed on an absurd page number.");
            var payload = (JsonObject)McpTestJson.Payload(result);

            var page = payload["page"]!.GetValue<int>();
            Assert.True(
                (long)(page - 1) * payload["pageSize"]!.GetValue<int>() <= int.MaxValue,
                $"{tool} let the row offset overflow (page {page}).");
            Assert.Empty((JsonArray)payload["items"]!);
        }
    }

    [Fact]
    public async Task ReportTools_ReturnTheDestinationAsData_WithoutOverwritingItWithTheBackLink()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var report = new ReportLink
        {
            Id = Guid.NewGuid(),
            Name = "Adoption dashboard",
            Description = "Weekly adoption",
            Url = "https://contoso.example/powerbi/adoption",
            Kind = ReportLinkKind.Report,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            LastEditedByUserId = user.Id,
            LastEditedAt = DateTime.UtcNow,
            Version = Guid.NewGuid(),
        };
        db.ReportLinks.Add(report);
        await db.SaveChangesAsync();

        var result = await McpTestHost.Create(db, user).CallToolAsync("get_report", "public-techniques", new { id = report.Id.ToString() });
        var payload = (JsonObject)McpTestJson.Payload(result);

        // FR-010: the linked destination is returned as stored data, never fetched — and the
        // gallery back-link lives in its own field so the two can never be confused.
        Assert.Equal("https://contoso.example/powerbi/adoption", payload["url"]!.GetValue<string>());
        Assert.Equal($"{McpTestHost.BaseUrl}/reports/{report.Id}?collectionId=public-techniques", payload["galleryUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownTool_And_UnknownMethod_AreProtocolErrors()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var unknownTool = await host.SendAsync("tools/call", new { name = "delete_everything", arguments = new { } });
        Assert.Equal(McpErrorCodes.InvalidParams, unknownTool!["error"]!["code"]!.GetValue<int>());
        Assert.Contains("delete_everything", unknownTool["error"]!["message"]!.GetValue<string>());

        var unknownMethod = await host.SendAsync("resources/list");
        Assert.Equal(McpErrorCodes.MethodNotFound, unknownMethod!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task BadArguments_AreReportedPrecisely_RatherThanSilentlyIgnored()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var badEnum = await host.SendAsync("tools/call", new
        {
            name = "search_tasks",
            arguments = new { collectionId = "public-techniques", taskType = "Sorcery" },
        });
        Assert.Equal(McpErrorCodes.InvalidParams, badEnum!["error"]!["code"]!.GetValue<int>());
        Assert.Contains("prompt", badEnum["error"]!["message"]!.GetValue<string>());

        var badGuid = await host.SendAsync("tools/call", new
        {
            name = "get_task",
            arguments = new { collectionId = "public-techniques", id = "not-a-guid" },
        });
        Assert.Equal(McpErrorCodes.InvalidParams, badGuid!["error"]!["code"]!.GetValue<int>());

        var unknownArgument = await host.SendAsync("tools/call", new
        {
            name = "search_methods",
            arguments = new { collectionId = "public-techniques", sortBy = "magic" },
        });
        Assert.Contains("sortBy", unknownArgument!["error"]!["message"]!.GetValue<string>());

        var missingId = await host.SendAsync("tools/call", new { name = "get_method", arguments = new { collectionId = "public-techniques" } });
        Assert.Contains("requires the 'id' argument", missingId!["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task MalformedAndNonConformingEnvelopes_GetTheRightJsonRpcCodes()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        Assert.Equal(McpErrorCodes.ParseError,
            (await host.SendRawAsync("{ not json"))!["error"]!["code"]!.GetValue<int>());

        Assert.Equal(McpErrorCodes.InvalidRequest,
            (await host.SendRawAsync("""{"method":"ping","id":1}"""))!["error"]!["code"]!.GetValue<int>());

        // Batching is not allowed from protocol revision 2025-06-18 onwards.
        Assert.Equal(McpErrorCodes.InvalidRequest,
            (await host.SendRawAsync("""[{"jsonrpc":"2.0","method":"ping","id":1}]"""))!["error"]!["code"]!.GetValue<int>());

        Assert.Equal(McpErrorCodes.InvalidRequest,
            (await host.SendRawAsync(""))!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Notification_IsAcceptedAndAudited_WithoutAResponseBody()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var result = await host.PostAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.IsType<AcceptedResult>(result);
        Assert.Null(McpTestJson.ReadBody(result));
        Assert.Equal("notifications/initialized", Assert.Single(host.Audit.Records).Method);
    }

    [Fact]
    public async Task DisablingTheServer_TurnsEveryVerbIntoAClear503()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db, enabled: false);

        var post = await host.PostAsync("""{"jsonrpc":"2.0","method":"tools/list","id":1}""");
        var unavailable = Assert.IsType<ObjectResult>(post);
        Assert.Equal(503, unavailable.StatusCode);
        Assert.Contains("not enabled", unavailable.Value!.ToString());

        Assert.Equal(503, Assert.IsType<ObjectResult>(host.Endpoint.Get(McpTestHost.Request(""))).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(host.Endpoint.Delete(McpTestHost.Request(""))).StatusCode);

        Assert.Equal(McpOutcome.Disabled, Assert.Single(host.Audit.Records).Outcome);
    }

    [Fact]
    public async Task GetDeclinesToStream_AndDeleteIsANoOp()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        var request = McpTestHost.Request("");
        var get = Assert.IsType<ObjectResult>(host.Endpoint.Get(request));
        Assert.Equal(405, get.StatusCode);
        Assert.Equal("POST, DELETE", request.HttpContext.Response.Headers["Allow"].ToString());

        Assert.IsType<NoContentResult>(host.Endpoint.Delete(McpTestHost.Request("")));
    }

    [Fact]
    public async Task EveryRequest_IsAttributedToTheHumanTheAgentActedFor()
    {
        // FR-004 / FR-009 / SC-005: the trail names a person, not an anonymous service.
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Dana Analyst");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user);
        await host.CallToolAsync("search_tasks", "public-techniques", new { q = "anything" }, signedInAs: user);

        var record = Assert.Single(host.Audit.Records);
        Assert.Equal("tools/call", record.Method);
        Assert.Equal("search_tasks", record.Tool);
        Assert.Equal("Dana Analyst", record.ActingUser);
        Assert.Equal($"{user.EntraTenantId ?? MethodTestSupport.TenantId}/{user.EntraObjectId}", record.ActingUserObjectId);
        Assert.Equal(McpOutcome.Ok, record.Outcome);
    }

    [Fact]
    public async Task AnUnexpectedFailure_ComesBackAsJsonRpc_AndIsStillAudited()
    {
        // Without a general catch, anything the delegated Function throws escapes as the host's raw
        // 500: the client reports a transport failure rather than one failed call, and the audit
        // trail loses exactly the requests worth investigating.
        await using var store = await RelationalTestDb.Create();
        var db = store.Context();
        var host = McpTestHost.Create(db);
        await db.DisposeAsync();

        var response = await host.SendAsync("tools/call", new { name = "search_tasks", arguments = new { collectionId = "public-techniques" } });

        var error = (JsonObject)response!["error"]!;
        Assert.Equal(McpErrorCodes.InternalError, error["code"]!.GetValue<int>());

        // The detail belongs in the log, not in the caller's response.
        var message = error["message"]!.GetValue<string>();
        Assert.DoesNotContain("Disposed", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WorkflowGallery.Api", message);

        Assert.Equal(McpOutcome.RpcError, Assert.Single(host.Audit.Records).Outcome);
    }

    [Fact]
    public async Task AFailedToolCall_IsAuditedAsSuch()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db);

        await host.CallToolAsync("get_task", "public-techniques", new { id = Guid.NewGuid().ToString() });

        Assert.Equal(McpOutcome.ToolError, Assert.Single(host.Audit.Records).Outcome);
    }

    internal static TaskItem SeedTask(AppDbContext db, User user, string name)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Produces a structured summary.",
            TaskType = "prompt",
            TargetIds = new() { "microsoft.copilot.m365" },
            InlineContent = "Summarise the following transcript.",
            Tags = ["summarisation"],
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            LastEditedByUserId = user.Id,
            LastEditedAt = DateTime.UtcNow.AddDays(-2),
        };

        db.Tasks.Add(task);
        return task;
    }

    internal static Workflow SeedWorkflow(AppDbContext db, User user, string name, TaskItem task, AiMethod? method = null)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProblemStatement = "Reviews take too long.",
            DesiredOutcome = "A reviewed summary within a day.",
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastEditedByUserId = user.Id,
            LastEditedAt = DateTime.UtcNow.AddDays(-1),
            MethodId = method?.Id,
        };

        workflow.Steps =
        [
            new WorkflowStep
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflow.Id,
                SequenceOrder = 1,
                TaskId = task.Id,
                TargetId = "microsoft.copilot.m365",
            },
            new WorkflowStep
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflow.Id,
                SequenceOrder = 2,
                IsManualStep = true,
                ManualStepTitle = "Review before sending",
                TargetId = null,
            },
        ];

        db.Workflows.Add(workflow);
        return workflow;
    }
}
