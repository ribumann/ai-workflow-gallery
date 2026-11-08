using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// User Story 2 for AI methods. Methods are the gallery's most re-used content — tasks and
/// workflows are built on them — and their endpoint guards concurrency with an opaque `version`
/// that it requires on every update, rather than the edit timestamp tasks and workflows use. These
/// tests pin that the tools honour that contract and never write back a stale read.
/// </summary>
public class McpMethodWriteTests
{
    [Fact]
    public async Task CreateMethod_RecordsIt_AttributedToTheSignedInPerson()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_method", "public-techniques", new
        {
            title = "Evidence synthesis",
            summary = "Combine several sources into one defensible answer.",
            approach = "# Approach\n\n1. Gather\n2. Compare\n3. Conclude",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var payload = (JsonObject)McpTestJson.Payload(result);
        Assert.Equal("Evidence synthesis", payload["title"]!.GetValue<string>());
        Assert.StartsWith(McpTestHost.BaseUrl, payload["galleryUrl"]!.GetValue<string>());

        var stored = await db.AiMethods.AsNoTracking().SingleAsync(m => m.Title == "Evidence synthesis");
        Assert.Equal(user.Id, stored.CreatedByUserId);
        Assert.Contains("Compare", stored.Approach, StringComparison.Ordinal);
    }

    /// <summary>The endpoint's own validation is what refuses this; the tool only surfaces it.</summary>
    [Fact]
    public async Task CreateMethod_IsRefused_WhenTheApproachIsMissing()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var response = await host.SendAsync("tools/call", new
        {
            name = "create_method",
            arguments = new { collectionId = "public-techniques", title = "Half a method", summary = "No approach given." },
        }, signedInAs: user);

        Assert.Contains("approach", response!["error"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.AiMethods.AnyAsync());
    }

    [Fact]
    public async Task UpdateMethod_ChangesOnlyWhatWasAsked()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        // Captured now: `method` is tracked by this context, so the endpoint's own edit mutates it.
        var versionBefore = method.Version;

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_method", "public-techniques", new
        {
            id = method.Id.ToString(),
            summary = "Combine several sources into one defensible answer.",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.AiMethods.AsNoTracking().SingleAsync(m => m.Id == method.Id);
        Assert.Equal("Combine several sources into one defensible answer.", stored.Summary);

        // Untouched by an edit that never mentioned them.
        Assert.Equal("Synthesis", stored.Title);
        Assert.Equal("# Approach\n\nCompare evidence.", stored.Approach);

        // Every accepted edit mints a new version, so a caller holding the old one is now stale.
        Assert.NotEqual(versionBefore, stored.Version);
    }

    /// <summary>
    /// A caller that supplies a version it read earlier is told plainly that the method moved on,
    /// rather than having its edit applied over the top.
    /// </summary>
    [Fact]
    public async Task UpdateMethod_IsRefusedAsAConflict_WhenTheSuppliedVersionIsStale()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_method", "public-techniques", new
        {
            id = method.Id.ToString(),
            title = "Based on a stale read",
            expectedVersion = Guid.NewGuid().ToString(),
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("changed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Synthesis", await db.AiMethods.AsNoTracking()
            .Where(m => m.Id == method.Id).Select(m => m.Title).SingleAsync());
    }

    /// <summary>
    /// The endpoint requires a version on every update, so the tool always sends one — and when the
    /// caller did not supply it, the only honest value is the one from this call's own read. That
    /// matters because the rest of the body is built from that same read: without the guard, a
    /// narrow edit would write a stale title and approach back over someone else's work and report
    /// success.
    /// </summary>
    [Fact]
    public async Task UpdateMethod_GuardsWithItsOwnRead_WhenTheCallerSuppliedNoVersion()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var tools = McpInterleavedHost.Create(db, user, async () =>
        {
            var concurrent = await db.AiMethods.SingleAsync(m => m.Id == method.Id);
            concurrent.Approach = "# Approach\n\nRewritten by someone else in the meantime.";
            concurrent.Version = Guid.NewGuid();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

        var result = await tools.ExecuteAsync(
            "update_method",
            new JsonObject { ["collectionId"] = "public-techniques", ["id"] = method.Id.ToString(), ["title"] = "Evidence synthesis" },
            McpTestHost.Request("{}", user),
            CancellationToken.None);

        Assert.True(result.IsError);

        var stored = await db.AiMethods.AsNoTracking().SingleAsync(m => m.Id == method.Id);
        Assert.Equal("Synthesis", stored.Title);
        Assert.Equal("# Approach\n\nRewritten by someone else in the meantime.", stored.Approach);
    }

    [Fact]
    public async Task DeleteMethod_DescribesTheImpactFirst_AndOnlyDeletesOnceConfirmed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var refused = await host.CallToolAsync("delete_method", "public-techniques", new { id = method.Id.ToString() }, user);

        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains("Synthesis", McpTestJson.Text(refused), StringComparison.Ordinal);
        Assert.True(await db.AiMethods.AsNoTracking().AnyAsync(m => m.Id == method.Id));

        var confirmed = await host.CallToolAsync(
            "delete_method", "public-techniques", new { id = method.Id.ToString(), confirm = true }, user);

        Assert.False(confirmed["isError"]!.GetValue<bool>());
        Assert.False(await db.AiMethods.AsNoTracking().AnyAsync(m => m.Id == method.Id));
    }

    /// <summary>
    /// Confirming is not an override. The endpoint refuses while anything is still based on the
    /// method, and lists what — which is what the person needs in order to unlink them.
    /// </summary>
    [Fact]
    public async Task DeleteMethod_IsStillRefused_WhenATaskIsBasedOnIt()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        var task = McpFunctionTests.SeedTask(db, user, "Draft release note");
        task.MethodId = method.Id;
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync(
            "delete_method", "public-techniques", new { id = method.Id.ToString(), confirm = true }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        var text = McpTestJson.Text(result);
        Assert.Contains("Draft release note", text, StringComparison.Ordinal);

        // The method endpoint has no confirm override, unlike the task one. Telling an agent that
        // already passed confirm: true to retry with it would send it round a loop that cannot
        // make progress, and invites it to claim the deletion can be forced.
        Assert.Contains("no override", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("if they agree", text, StringComparison.OrdinalIgnoreCase);

        Assert.True(await db.AiMethods.AsNoTracking().AnyAsync(m => m.Id == method.Id));
    }

    /// <summary>
    /// Only the creator or an administrator may delete a method, and the endpoint says so with a
    /// bare 403 carrying no body at all. An agent has to be told what that means, not handed
    /// "the gallery returned an unexpected response".
    /// </summary>
    [Fact]
    public async Task DeleteMethod_ExplainsTheRefusal_WhenSomeoneElseCreatedIt()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var author = User("Ada Lovelace");
        var other = User("Charles Babbage");
        db.Users.AddRange(author, other);
        var method = Method(author, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, other, writesEnabled: true);

        var result = await host.CallToolAsync(
            "delete_method", "public-techniques", new { id = method.Id.ToString(), confirm = true }, other);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("administrator", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unexpected", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.AiMethods.AsNoTracking().AnyAsync(m => m.Id == method.Id));
    }

    [Fact]
    public async Task MethodWrites_AreRefused_WhenNoSignedInPersonCanBeNamed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        // No signedInAs, so no principal header reaches the delegated endpoint.
        var result = await host.CallToolAsync("create_method", "public-techniques", new
        {
            title = "Anonymous method",
            summary = "Should never be stored.",
            approach = "# Approach",
        });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("signed-in", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.AiMethods.AnyAsync());
    }

    [Fact]
    public async Task MethodWriteTools_AreRefused_WhenWritesAreDisabled()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var method = Method(user, "Synthesis");
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: false);

        foreach (var tool in new[] { "create_method", "update_method", "delete_method" })
        {
            var response = await host.SendAsync("tools/call", new
            {
                name = tool,
                arguments = new { collectionId = "public-techniques", id = method.Id.ToString() },
            }, signedInAs: user);

            Assert.NotNull(response!["error"]);
        }

        Assert.Equal("Synthesis", await db.AiMethods.AsNoTracking()
            .Where(m => m.Id == method.Id).Select(m => m.Title).SingleAsync());
    }
}
