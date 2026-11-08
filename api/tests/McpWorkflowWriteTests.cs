using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// User Story 2 for workflows. A workflow is a sequence, which makes the write contract harsher
/// than a task's: the endpoint rebuilds every step on every save and refuses anything shorter than
/// two steps, so an agent that means to change only the name must still submit the whole sequence.
/// These tests pin that it does so without disturbing it.
/// </summary>
public class McpWorkflowWriteTests
{
    private static object TaskStep(Guid taskId, string target = "microsoft.copilot.m365") =>
        new { taskId = taskId.ToString(), targetId = target };

    private static object ManualStep(string title) =>
        new { isManualStep = true, manualStepTitle = title, targetId = (string?)null };

    [Fact]
    public async Task CreateWorkflow_RecordsTheSequence_AttributedToTheSignedInPerson()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_workflow", "public-techniques", new
        {
            name = "Release comms",
            problemStatement = "Release notes go out late.",
            desiredOutcome = "Notes reviewed and published on the day.",
            steps = new object[]
            {
                TaskStep(task.Id),
                ManualStep("Review before sending"),
            },
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var payload = (JsonObject)McpTestJson.Payload(result);
        Assert.Equal("Release comms", payload["name"]!.GetValue<string>());
        Assert.Equal(2, ((JsonArray)payload["steps"]!).Count);
        Assert.StartsWith(McpTestHost.BaseUrl, payload["galleryUrl"]!.GetValue<string>());

        var stored = await db.Workflows.AsNoTracking()
            .Include(w => w.Steps)
            .SingleAsync(w => w.Name == "Release comms");

        Assert.Equal(user.Id, stored.CreatedByUserId);
        Assert.Equal([1, 2], stored.Steps.OrderBy(s => s.SequenceOrder).Select(s => s.SequenceOrder));
        Assert.Equal(task.Id, stored.Steps.Single(s => s.SequenceOrder == 1).TaskId);
        Assert.True(stored.Steps.Single(s => s.SequenceOrder == 2).IsManualStep);
    }

    /// <summary>
    /// The endpoint refuses a workflow with fewer than two steps, and says why. The tool surfaces
    /// that rather than inventing a second rule of its own.
    /// </summary>
    [Fact]
    public async Task CreateWorkflow_IsRefused_WhenTheSequenceIsTooShort()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_workflow", "public-techniques", new
        {
            name = "Too short",
            problemStatement = "One step is not a workflow.",
            desiredOutcome = "Nothing.",
            steps = new object[] { TaskStep(task.Id) },
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("at least 2 steps", McpTestJson.Text(result), StringComparison.Ordinal);
        Assert.False(await db.Workflows.AnyAsync(w => w.Name == "Too short"));
    }

    /// <summary>
    /// An agent that invents a task identifier must be told, not quietly given a workflow with a
    /// step pointing nowhere.
    /// </summary>
    [Fact]
    public async Task CreateWorkflow_IsRefused_WhenAStepPointsAtATaskThatDoesNotExist()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);
        var invented = Guid.NewGuid();

        var result = await host.CallToolAsync("create_workflow", "public-techniques", new
        {
            name = "Invented step",
            problemStatement = "Points at nothing.",
            desiredOutcome = "Nothing.",
            steps = new object[] { TaskStep(task.Id), TaskStep(invented) },
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains(invented.ToString(), McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Workflows.AnyAsync(w => w.Name == "Invented step"));
    }

    /// <summary>
    /// A step that is neither task-backed nor manual is refused with the step's position, because
    /// the endpoint's own message does not say which of them is wrong.
    /// </summary>
    [Fact]
    public async Task CreateWorkflow_NamesTheStep_WhenOneIsNeitherTaskBackedNorManual()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_workflow",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Headless step",
                problemStatement = "Second step has no task.",
                desiredOutcome = "Nothing.",
                steps = new object[] { TaskStep(task.Id), new { targetId = "microsoft.copilot.cowork" } },
            },
        }, signedInAs: user);

        var message = response!["error"]!.ToJsonString();
        Assert.Contains("Step 2", message, StringComparison.Ordinal);
        Assert.False(await db.Workflows.AnyAsync(w => w.Name == "Headless step"));
    }

    /// <summary>
    /// The heart of this change. Renaming a workflow must not disturb its sequence, even though the
    /// endpoint rebuilds every step on every save and would reject a save carrying none.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflow_ChangesOnlyWhatWasAsked_AndLeavesTheSequenceIntact()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_workflow", "public-techniques", new
        {
            id = workflow.Id.ToString(),
            name = "Release communications",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.Workflows.AsNoTracking()
            .Include(w => w.Steps)
            .SingleAsync(w => w.Id == workflow.Id);

        Assert.Equal("Release communications", stored.Name);

        // Untouched by a rename.
        Assert.Equal("Reviews take too long.", stored.ProblemStatement);
        Assert.Equal(2, stored.Steps.Count);

        var ordered = stored.Steps.OrderBy(s => s.SequenceOrder).ToList();
        Assert.Equal(task.Id, ordered[0].TaskId);
        Assert.Equal("microsoft.copilot.m365", ordered[0].TargetId);
        Assert.True(ordered[1].IsManualStep);
        Assert.Equal("Review before sending", ordered[1].ManualStepTitle);
        Assert.Null(ordered[1].TargetId);
    }

    /// <summary>Supplying steps replaces the sequence outright — there is no partial step edit.</summary>
    [Fact]
    public async Task UpdateWorkflow_ReplacesTheWholeSequence_WhenStepsAreSupplied()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_workflow", "public-techniques", new
        {
            id = workflow.Id.ToString(),
            steps = new object[]
            {
                ManualStep("Gather the facts"),
                TaskStep(task.Id),
                ManualStep("Send it"),
            },
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.Workflows.AsNoTracking()
            .Include(w => w.Steps)
            .SingleAsync(w => w.Id == workflow.Id);

        var ordered = stored.Steps.OrderBy(s => s.SequenceOrder).ToList();
        Assert.Equal(3, ordered.Count);
        Assert.Equal("Gather the facts", ordered[0].ManualStepTitle);
        Assert.Equal(task.Id, ordered[1].TaskId);
        Assert.Equal("Send it", ordered[2].ManualStepTitle);

        // The name was not mentioned, so it stands.
        Assert.Equal("Release comms", stored.Name);
    }

    /// <summary>
    /// A step whose task was deleted cannot be expressed as write input — the browser cannot save
    /// such a workflow either. Refusing with an explanation is the honest outcome; the alternative
    /// is a save that quietly turns the orphan into something else.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflow_ExplainsItself_WhenAStepsTaskHasBeenDeleted()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        // Delete the task the way the gallery does: the step survives, holding a name snapshot.
        var orphaned = await db.WorkflowSteps.SingleAsync(s => s.TaskId == task.Id);
        orphaned.TaskId = null;
        orphaned.TaskNameSnapshot = task.Name;
        orphaned.TaskTypeSnapshot = task.TaskType;
        db.Tasks.Remove(await db.Tasks.SingleAsync(t => t.Id == task.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_workflow", "public-techniques", new
        {
            id = workflow.Id.ToString(),
            name = "Release communications",
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("deleted", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);

        // Nothing was written.
        Assert.Equal("Release comms", await db.Workflows.AsNoTracking()
            .Where(w => w.Id == workflow.Id).Select(w => w.Name).SingleAsync());
    }

    /// <summary>A stale edit is reported as a conflict rather than overwriting newer work (AS3).</summary>
    [Fact]
    public async Task UpdateWorkflow_IsRefusedAsAConflict_WhenSomeoneElseEditedFirst()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_workflow", "public-techniques", new
        {
            id = workflow.Id.ToString(),
            name = "Based on a stale read",
            expectedLastEditedAt = workflow.LastEditedAt.AddHours(-2).ToString("o"),
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("changed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Release comms", await db.Workflows.AsNoTracking()
            .Where(w => w.Id == workflow.Id).Select(w => w.Name).SingleAsync());
    }

    /// <summary>
    /// Nothing downstream guards a workflow deletion — the endpoint removes it and cascades the
    /// steps without asking — so the tool is the only place a person gets to intervene.
    /// </summary>
    [Fact]
    public async Task DeleteWorkflow_DescribesTheImpactFirst_AndOnlyDeletesOnceConfirmed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var refused = await host.CallToolAsync("delete_workflow", "public-techniques", new { id = workflow.Id.ToString() }, user);

        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains("Release comms", McpTestJson.Text(refused), StringComparison.Ordinal);
        Assert.Contains("2 step", McpTestJson.Text(refused), StringComparison.Ordinal);
        Assert.True(await db.Workflows.AsNoTracking().AnyAsync(w => w.Id == workflow.Id));

        var confirmed = await host.CallToolAsync(
            "delete_workflow", "public-techniques", new { id = workflow.Id.ToString(), confirm = true }, user);

        Assert.False(confirmed["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        Assert.False(await db.Workflows.AnyAsync(w => w.Id == workflow.Id));

        // The task the sequence referred to is untouched.
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == task.Id));
    }

    [Fact]
    public async Task WorkflowWrites_AreRefused_WhenNoSignedInPersonCanBeNamed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_workflow", "public-techniques", new
        {
            name = "Unattributable",
            problemStatement = "Should never be stored.",
            desiredOutcome = "Nothing.",
            steps = new object[] { TaskStep(task.Id), ManualStep("Review") },
        });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("attributed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Workflows.AnyAsync(w => w.Name == "Unattributable"));
    }

    [Fact]
    public async Task WorkflowWriteTools_AreRefused_WhenWritesAreDisabled()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: false);

        foreach (var name in new[] { "create_workflow", "update_workflow", "delete_workflow" })
        {
            var response = await host.SendAsync("tools/call", new
            {
                name,
                arguments = new { collectionId = "public-techniques", id = workflow.Id.ToString() },
            }, signedInAs: user);

            // Gated tools are indistinguishable from tools that do not exist, so a disabled server
            // never hints at a capability a caller could go looking for.
            Assert.NotNull(response!["error"]);
            Assert.Contains("Unknown tool", response["error"]!.ToJsonString(), StringComparison.Ordinal);
        }

        Assert.True(await db.Workflows.AsNoTracking().AnyAsync(w => w.Id == workflow.Id));
    }

    /// <summary>
    /// Nested arguments are validated with the same vocabulary as top-level ones, and the message
    /// names the element that was wrong — "steps[1].toolSurface", not just "toolSurface".
    /// </summary>
    [Fact]
    public async Task CreateWorkflow_NamesTheOffendingStep_WhenANestedArgumentIsWrong()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_workflow",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Bad surface",
                problemStatement = "Second step names a surface that does not exist.",
                desiredOutcome = "Nothing.",
                steps = new object[]
                {
                    TaskStep(task.Id),
                    new { isManualStep = true, manualStepTitle = "Review", targetId = "TelepathyCopilot" },
                },
            },
        }, signedInAs: user);

        Assert.Contains("steps[1].targetId", response!["error"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.False(await db.Workflows.AnyAsync(w => w.Name == "Bad surface"));
    }

    /// <summary>
    /// Re-submitting the existing sequence is what makes a partial update possible at all, but it
    /// also means an agent that read the workflow a moment ago would overwrite anything someone
    /// else committed in between — the whole curated sequence, silently, with no conflict raised.
    /// The browser cannot do that: its form is populated from a read and always submits that read's
    /// timestamp. So when the caller supplies neither a guard nor steps of their own, the tool
    /// guards with the timestamp from its own pre-read.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflow_GuardsWithItsOwnRead_WhenTheCallerSuppliedNoTimestampAndNoSteps()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        // Someone else edits the workflow in the window between the tool's read and its write.
        var tools = BuildTools(db, user, async () =>
        {
            var concurrent = await db.Workflows.SingleAsync(w => w.Id == workflow.Id);
            concurrent.DesiredOutcome = "Edited by someone else in the meantime.";
            concurrent.LastEditedAt = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

        var result = await tools.ExecuteAsync(
            "update_workflow",
            new JsonObject { ["collectionId"] = "public-techniques", ["id"] = workflow.Id.ToString(), ["name"] = "Release communications" },
            McpTestHost.Request("{}", user),
            CancellationToken.None);

        Assert.True(result.IsError);

        var stored = await db.Workflows.AsNoTracking().SingleAsync(w => w.Id == workflow.Id);
        Assert.Equal("Release comms", stored.Name);
        Assert.Equal("Edited by someone else in the meantime.", stored.DesiredOutcome);
    }

    /// <summary>
    /// The same interleaving, but the caller supplied the sequence themselves. Nothing of theirs is
    /// stale, so last-write-wins stands and the update goes through, exactly as the tool advertises.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflow_StillAllowsLastWriteWins_WhenTheCallerSuppliedTheSteps()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var tools = BuildTools(db, user, async () =>
        {
            var concurrent = await db.Workflows.SingleAsync(w => w.Id == workflow.Id);
            concurrent.LastEditedAt = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

        var result = await tools.ExecuteAsync(
            "update_workflow",
            new JsonObject
            {
                ["collectionId"] = "public-techniques",
                ["id"] = workflow.Id.ToString(),
                ["steps"] = new JsonArray(
                    new JsonObject { ["isManualStep"] = true, ["manualStepTitle"] = "Gather the facts", ["targetId"] = null },
                    new JsonObject { ["taskId"] = task.Id.ToString(), ["targetId"] = "microsoft.copilot.m365" }),
            },
            McpTestHost.Request("{}", user),
            CancellationToken.None);

        Assert.False(result.IsError);

        var stored = await db.Workflows.AsNoTracking().Include(w => w.Steps)
            .SingleAsync(w => w.Id == workflow.Id);
        Assert.Equal("Gather the facts", stored.Steps.OrderBy(s => s.SequenceOrder).First().ManualStepTitle);
    }

    /// <summary>
    /// Builds the tool layer over a <see cref="Workflows"/> that runs a hook the moment a read
    /// completes, which is the only way to observe what the tool does with the window between
    /// reading a workflow and writing it back.
    /// </summary>
    private static IGalleryMcpTools BuildTools(AppDbContext db, User user, Func<Task> afterRead) =>
        McpInterleavedHost.Create(db, user, afterRead);
}
