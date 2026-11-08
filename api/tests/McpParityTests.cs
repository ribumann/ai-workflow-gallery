using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// SC-002: an agent must see exactly what the same person sees in the browser — no more, no less.
///
/// <para>
/// These tests call the very endpoint the browser calls and compare it, field for field, with what
/// the MCP tool returns for the same arguments and the same principal. The MCP layer delegates to
/// those endpoints precisely so this can be asserted rather than hoped for.
/// </para>
/// </summary>
public class McpParityTests
{
    [Fact]
    public async Task EveryReadTool_ReturnsByteForByteWhatTheBrowserEndpointReturns()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Parity Tester");
        var method = Method(user, "Chain of thought");
        db.Users.Add(user);
        db.AiMethods.Add(method);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task, method);
        var report = new ReportLink
        {
            Id = Guid.NewGuid(),
            Name = "Usage report",
            Description = "Usage over time",
            Url = "https://contoso.example/report",
            Kind = ReportLinkKind.Report,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            LastEditedByUserId = user.Id,
            LastEditedAt = DateTime.UtcNow,
            Version = Guid.NewGuid(),
        };
        db.ReportLinks.Add(report);
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService(user);
        var tasks = new Tasks(db, currentUser, new FakeBlobStorageService(), new FakeFileValidationService());
        var workflows = new Workflows(db, currentUser);
        var methods = new Methods(db, currentUser);
        var reports = new Reports(db, currentUser);
        var host = McpTestHost.Create(db, user);
        Microsoft.AspNetCore.Http.HttpRequest BrowserRequest() => Principal(Query(), user);

        var cases = new (string Tool, object Arguments, Func<Task<IActionResult>> Browser)[]
        {
            ("search_tasks", new { }, () => tasks.List(BrowserRequest(), Ct)),
            ("get_task", new { id = task.Id.ToString() }, () => tasks.GetById(BrowserRequest(), task.Id, Ct)),
            ("search_workflows", new { }, () => workflows.List(BrowserRequest(), Ct)),
            ("get_workflow", new { id = workflow.Id.ToString() }, () => workflows.GetById(BrowserRequest(), workflow.Id, Ct)),
            ("search_methods", new { }, () => methods.List(BrowserRequest(), Ct)),
            ("get_method", new { id = method.Id.ToString() }, () => methods.GetById(BrowserRequest(), method.Id, Ct)),
            ("search_reports", new { }, () => reports.List(BrowserRequest(), Ct)),
            ("get_report", new { id = report.Id.ToString() }, () => reports.GetById(BrowserRequest(), report.Id, Ct)),
        };

        foreach (var (tool, arguments, browser) in cases)
        {
            var expected = Canonicalize(((ObjectResult)await browser()).Value!);
            var actual = (JsonObject)McpTestJson.Payload(await host.CallToolAsync(tool, "public-techniques", arguments, user));

            // The back-link is the one thing MCP adds; strip it and the rest must be identical.
            StripGalleryUrls(actual);

            Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
        }
    }

    [Fact]
    public async Task ATaskTheBrowserCannotSee_IsAlsoInvisibleToTheAgent()
    {
        // Parity has to hold for absence too, otherwise the MCP surface would be a way around the
        // gallery's own visibility rules (FR-003).
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var browser = await new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService())
            .List(Principal(Query(), user), Ct);
        var browserItems = (JsonArray)Canonicalize(((ObjectResult)browser).Value!)["items"]!;

        var agent = (JsonObject)McpTestJson.Payload(
            await McpTestHost.Create(db, user).CallToolAsync("search_tasks", "public-techniques", new { }, user));

        Assert.Empty(browserItems);
        Assert.Empty((JsonArray)agent["items"]!);
    }

    /// <summary>Serializes a DTO the way the HTTP pipeline does, so the comparison is of wire shapes.</summary>
    private static JsonObject Canonicalize(object value) =>
        (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(value, GalleryMcpTools.PayloadOptions))!;

    private static void StripGalleryUrls(JsonObject payload)
    {
        payload.Remove("galleryUrl");

        if (payload["items"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                item.Remove("galleryUrl");
            }
        }
    }
}
