using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// User Story 2: an agent contributes back. A write made through MCP must be indistinguishable
/// from the same change made in the browser (AS1), refused when the caller cannot be named (AS2,
/// AS4), and refused as a conflict when it is stale rather than overwriting newer work (AS3).
/// </summary>
public class McpTaskWriteTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task CreateTask_RecordsTheTask_AttributedToTheSignedInPerson()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Summarise a transcript",
            description = "Turns a raw meeting transcript into decisions and actions.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
            tags = new[] { "summarisation", "meetings" },
            inlineContent = "Summarise the following transcript.",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var payload = (JsonObject)McpTestJson.Payload(result);
        Assert.Equal("Summarise a transcript", payload["name"]!.GetValue<string>());
        Assert.Equal("Ada Lovelace", payload["createdBy"]!["displayName"]!.GetValue<string>());
        Assert.Equal(["summarisation", "meetings"], ((JsonArray)payload["tags"]!).Select(t => t!.GetValue<string>()));

        // The back-link is what makes the contribution reviewable by a person afterwards.
        Assert.StartsWith(McpTestHost.BaseUrl, payload["galleryUrl"]!.GetValue<string>());

        var stored = await db.Tasks.SingleAsync(t => t.Name == "Summarise a transcript");
        Assert.Equal(user.Id, stored.CreatedByUserId);
        Assert.Equal(user.Id, stored.LastEditedByUserId);
    }

    [Fact]
    public async Task CreateTask_IsRefused_WhenNoSignedInPersonCanBeNamed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        // No principal header: the gallery's own fallback would attribute this to a shared
        // "Anonymous contributor", which SC-005 does not accept for an agent-made write.
        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Unattributable",
            description = "Should never be stored.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
            inlineContent = "…",
        });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("attributed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Unattributable"));
    }

    [Fact]
    public async Task WriteTools_AreRefused_WhenWritesAreDisabled()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: false);

        var response = await host.SendAsync("tools/call",
            new { name = "delete_task", arguments = new { collectionId = "public-techniques", id = task.Id.ToString() } }, signedInAs: user);

        // Gated tools are unresolvable, not merely unadvertised — so this is a protocol-level
        // "no such tool", exactly as if delete_task had never been written (FR-007).
        Assert.NotNull(response!["error"]);
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == task.Id));
    }

    [Fact]
    public async Task UpdateTask_ChangesOnlyTheFieldsSupplied()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        task.ReferenceLink = "https://contoso.example/playbook";
        task.DataSource = "Release backlog";
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            description = "Now also covers hotfix notes.",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        var stored = await db.Tasks.SingleAsync(t => t.Id == task.Id);

        Assert.Equal("Now also covers hotfix notes.", stored.Description);

        // The endpoint is a full replace, so everything not mentioned would be wiped if the tool
        // simply forwarded the caller's arguments. Curated content must survive a narrow edit.
        Assert.Equal("Draft release note", stored.Name);
        Assert.Equal("https://contoso.example/playbook", stored.ReferenceLink);
        Assert.Equal("Release backlog", stored.DataSource);
        Assert.Equal("Summarise the following transcript.", stored.InlineContent);
        Assert.Equal(["summarisation"], stored.Tags);
        Assert.Equal("prompt", stored.TaskType);
    }

    [Fact]
    public async Task UpdateTask_ClearsAField_WhenItIsExplicitlyBlanked()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        task.DataSource = "Release backlog";
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        // "Leave it alone" is an absent argument; "remove it" is an explicitly empty one. Without
        // that distinction an agent could never clear a field it had set by mistake.
        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            dataSource = "",
            tags = Array.Empty<string>(),
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        var stored = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Null(stored.DataSource);
        Assert.Empty(stored.Tags);
        Assert.Equal("Draft release note", stored.Name);
    }

    [Fact]
    public async Task UpdateTask_KeepsTheMethodLink_ThroughAnUnrelatedEdit()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        var method = Method(user, "Chain of thought");
        db.Users.Add(user);
        db.AiMethods.Add(method);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        task.MethodId = method.Id;
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            description = "Reworded.",
        }, user);

        Assert.Equal(method.Id, (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).MethodId);

        // …and an explicitly empty value is how the link is removed.
        var cleared = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            methodId = "",
        }, user);

        Assert.False(cleared["isError"]!.GetValue<bool>());
        Assert.Null((await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).MethodId);
    }

    [Fact]
    public async Task UpdateTask_TreatsAnExplicitNullAsUnsupplied_NotAsAnInstructionToClear()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        task.DataSource = "Release backlog";
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        // Language-model clients routinely fill every declared optional argument with null. Reading
        // that as "remove it" would make a narrow edit quietly destroy curated content.
        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            description = "Reworded.",
            dataSource = (string?)null,
            tags = (string[]?)null,
            referenceLink = (string?)null,
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        var stored = await db.Tasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Release backlog", stored.DataSource);
        Assert.Equal(["summarisation"], stored.Tags);
    }

    [Fact]
    public async Task UpdateTask_IsRefusedAsAConflict_WhenItIsStale()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var stale = task.LastEditedAt.AddMinutes(-5);
        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            description = "Based on a version I read five minutes ago.",
            expectedLastEditedAt = stale.ToString("o"),
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());

        // The conflict must hand back enough to retry, not just say "409".
        var text = McpTestJson.Text(result);
        Assert.Contains("expectedLastEditedAt", text, StringComparison.Ordinal);
        Assert.Contains("lastEditedAt", text, StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        Assert.Equal(
            "Produces a structured summary.",
            (await db.Tasks.SingleAsync(t => t.Id == task.Id)).Description);
    }

    [Fact]
    public async Task UpdateTask_Succeeds_WhenTheExpectedTimestampStillMatches()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var current = (JsonObject)McpTestJson.Payload(
            await host.CallToolAsync("get_task", "public-techniques", new { id = task.Id.ToString() }, user));

        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = task.Id.ToString(),
            description = "Freshly read, then edited.",
            expectedLastEditedAt = current["lastEditedAt"]!.GetValue<string>(),
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        Assert.Equal(
            "Freshly read, then edited.",
            (await db.Tasks.SingleAsync(t => t.Id == task.Id)).Description);
    }

    [Fact]
    public async Task UpdateTask_ReportsAMissingTaskAsNotFound_WithoutAttemptingTheWrite()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);
        var missing = Guid.NewGuid();

        var result = await host.CallToolAsync("update_task", "public-techniques", new
        {
            id = missing.ToString(),
            description = "Nothing to update.",
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains(missing.ToString(), McpTestJson.Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteTask_RemovesAnUnusedTask()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("delete_task", "public-techniques", new { id = task.Id.ToString() }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        // 204 carries no body, but an empty success would be indistinguishable from "nothing
        // happened" — so the tool states the outcome explicitly.
        var payload = (JsonObject)McpTestJson.Payload(result);
        Assert.True(payload["deleted"]!.GetValue<bool>());
        Assert.Equal(task.Id.ToString(), payload["id"]!.GetValue<string>());

        db.ChangeTracker.Clear();
        Assert.False(await db.Tasks.AnyAsync(t => t.Id == task.Id));
    }

    [Fact]
    public async Task DeleteTask_IsRefused_UntilTheImpactOnWorkflowsIsConfirmed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        var workflow = McpFunctionTests.SeedWorkflow(db, user, "Release comms", task);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var refused = await host.CallToolAsync("delete_task", "public-techniques", new { id = task.Id.ToString() }, user);

        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains(workflow.Name, McpTestJson.Text(refused), StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        Assert.True(await db.Tasks.AnyAsync(t => t.Id == task.Id));

        var confirmed = await host.CallToolAsync(
            "delete_task", "public-techniques", new { id = task.Id.ToString(), confirm = true }, user);

        Assert.False(confirmed["isError"]!.GetValue<bool>());

        db.ChangeTracker.Clear();
        Assert.False(await db.Tasks.AnyAsync(t => t.Id == task.Id));

        // The workflow keeps the step, showing the task's last-known name.
        var step = await db.WorkflowSteps.FirstAsync(s => s.WorkflowId == workflow.Id && s.TaskId == null);
        Assert.Equal("Draft release note", step.TaskNameSnapshot);
    }

    [Fact]
    public async Task CreateTask_SurfacesTheGallerysOwnValidationMessage()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        // Neither a reference link nor inline content, and MCP cannot attach a file.
        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Incomplete",
            description = "Has nowhere to point.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("At least one of", McpTestJson.Text(result), StringComparison.Ordinal);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Incomplete"));
    }

    [Fact]
    public async Task WriteTools_RejectArgumentsTheyDoNotDeclare()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_task",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Typo",
                description = "Has a misspelled argument.",
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                referenceLnk = "https://contoso.example",
            },
        }, signedInAs: user);

        // A silently ignored argument would mean the link the person asked for never got stored.
        Assert.NotNull(response!["error"]);
        Assert.Contains("referenceLnk", response["error"]!.ToJsonString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity on the incoming MCP request has to reach the delegated gallery endpoint, which
    /// only happens because the synthetic request copies the principal header across. Every other
    /// write test uses a fake current-user service that ignores the request entirely, so none of
    /// them would notice if that copy were dropped — and the failure mode in production is silent:
    /// contributions pile up under the shared "Anonymous contributor". This test resolves identity
    /// the way production does, so it fails if the forwarding is lost.
    /// </summary>
    [Fact]
    public async Task CreateTask_AttributesTheWrite_ToThePrincipalOnTheIncomingRequest()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var author = User("Grace Hopper");
        var bystander = User("Ada Lovelace");
        db.Users.AddRange(author, bystander);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, writesEnabled: true, resolveUserFromRequest: true);

        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Forwarded attribution",
            description = "Must be recorded against the caller on the request.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
            inlineContent = "…",
        }, author);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Name == "Forwarded attribution");
        Assert.Equal(author.Id, stored.CreatedByUserId);
        Assert.NotEqual(bystander.Id, stored.CreatedByUserId);

        // And no anonymous row was conjured up along the way.
        Assert.False(await db.Users.AnyAsync(
            u => u.EntraObjectId == Guid.Empty.ToString()));
    }

    /// <summary>
    /// The anonymous sentinel is a well-known constant, so a principal can carry it. Treating that
    /// as "named" would route the write to the shared row the guard exists to keep agents out of.
    /// </summary>
    [Fact]
    public async Task CreateTask_IsRefused_WhenThePrincipalCarriesTheAnonymousSentinel()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var anonymous = new User
        {
            Id = Guid.NewGuid(),
            EntraObjectId = Guid.Empty.ToString(),
            DisplayName = "Anonymous contributor",
        };
        db.Users.Add(anonymous);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, writesEnabled: true, resolveUserFromRequest: true);

        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Sentinel",
            description = "Should never be stored.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
            inlineContent = "…",
        }, anonymous);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("attributed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Sentinel"));
    }

    /// <summary>
    /// A model writing more prose than the column holds is ordinary, not an edge case. Caught at
    /// the boundary it is a precise, actionable message; left to SQL Server it is a truncation
    /// error the agent only ever sees as an opaque internal error. (The suite runs on SQLite, which
    /// ignores the length constraint entirely — so only this boundary check can catch it here.)
    /// </summary>
    [Fact]
    public async Task CreateTask_IsRefused_WhenAFieldIsLongerThanItsColumn()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_task",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Overlong",
                description = new string('x', 2001),
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                inlineContent = "…",
            },
        }, signedInAs: user);

        var error = response!["error"]!;
        Assert.Contains("2000 characters", error.ToJsonString(), StringComparison.Ordinal);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Overlong"));
    }

    /// <summary>
    /// Tags are stored joined by commas and split apart again on read, and nothing between the tool
    /// and the column guards that delimiter. The browser cannot produce such a tag — its editor
    /// splits the typed input on commas before submitting — so this is a hazard only an API caller
    /// reaches, and a plausible one: the field's own UI hint is "comma-separated". Unchecked, one
    /// tag silently becomes two on the next read, and every later edit re-sends the split pair as
    /// though someone had curated it.
    /// </summary>
    [Theory]
    [InlineData("reporting, automation")]
    [InlineData("cost,savings")]
    public async Task CreateTask_IsRefused_WhenATagContainsTheReservedDelimiter(string tag)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_task",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Comma tag",
                description = "Tags must survive a round-trip unchanged.",
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                inlineContent = "…",
                tags = new[] { tag },
            },
        }, signedInAs: user);

        Assert.Contains("comma", response!["error"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Comma tag"));
    }

    /// <summary>
    /// Several MCP clients collapse a one-item list to a bare string, so the tool accepts that —
    /// which makes "tags": "a, b" the most likely way the delimiter gets in.
    /// </summary>
    [Fact]
    public async Task CreateTask_IsRefused_WhenTagsArriveAsOneCommaSeparatedString()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_task",
            arguments = new
            {
                collectionId = "public-techniques",
                name = "Collapsed tags",
                description = "Tags must survive a round-trip unchanged.",
                taskType = "prompt",
                targetIds = new[] { "microsoft.copilot.m365" },
                inlineContent = "…",
                tags = "reporting, automation",
            },
        }, signedInAs: user);

        Assert.Contains("comma", response!["error"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.Tasks.AnyAsync(t => t.Name == "Collapsed tags"));
    }

    /// <summary>A well-formed tag list round-trips through the delimiter column unchanged.</summary>
    [Fact]
    public async Task CreateTask_StoresTagsThatSurviveTheRoundTrip()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_task", "public-techniques", new
        {
            name = "Round-trip tags",
            description = "Tags must survive a round-trip unchanged.",
            taskType = "prompt",
            targetIds = new[] { "microsoft.copilot.m365" },
            inlineContent = "…",
            tags = new[] { "reporting", "automation" },
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.Tasks.AsNoTracking().SingleAsync(t => t.Name == "Round-trip tags");
        Assert.Equal(["reporting", "automation"], stored.Tags);
    }

    /// <summary>
    /// The initialize instructions are the highest-trust guidance a client gets. Claiming the
    /// server is read-only while writes are enabled could persuade a client that a create or delete
    /// needs no human confirmation, so the claim has to follow the actual setting.
    /// </summary>
    [Fact]
    public async Task Initialize_AnnouncesWrites_WhenWritesAreEnabled()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var host = McpTestHost.Create(db, writesEnabled: true);

        var response = await host.SendAsync("initialize", new
        {
            protocolVersion = "2025-06-18",
            clientInfo = new { name = "probe", version = "1.0" },
            capabilities = new { },
        });

        var instructions = ((JsonObject)response!["result"]!)["instructions"]!.GetValue<string>();
        Assert.DoesNotContain("read-only", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirm", instructions, StringComparison.OrdinalIgnoreCase);
    }
}
