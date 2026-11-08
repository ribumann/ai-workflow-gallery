using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// User Story 2 for the directory of reports and AI apps. These entries are metadata and a link;
/// the gallery never fetches what is behind that link (FR-010), which is why an agent has to supply
/// the description itself. Like AI methods, they version with an opaque `version` the endpoint
/// requires on every update.
/// </summary>
public class McpReportWriteTests
{
    private static ReportLink Report(User user, string name = "Adoption dashboard") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = "Weekly adoption by business group.",
        Url = "https://contoso.example.com/adoption",
        Kind = ReportLinkKind.Report,
        Version = Guid.NewGuid(),
        CreatedByUser = user,
        CreatedByUserId = user.Id,
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        LastEditedByUser = user,
        LastEditedByUserId = user.Id,
        LastEditedAt = DateTime.UtcNow.AddDays(-1),
    };

    [Fact]
    public async Task CreateReport_RecordsIt_AttributedToTheSignedInPerson()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_report", "public-techniques", new
        {
            name = "Adoption dashboard",
            description = "Weekly adoption by business group.",
            url = "https://contoso.example.com/adoption",
            kind = "Report",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var payload = (JsonObject)McpTestJson.Payload(result);
        Assert.Equal("Adoption dashboard", payload["name"]!.GetValue<string>());
        Assert.StartsWith(McpTestHost.BaseUrl, payload["galleryUrl"]!.GetValue<string>());

        var stored = await db.ReportLinks.AsNoTracking().SingleAsync(r => r.Name == "Adoption dashboard");
        Assert.Equal(user.Id, stored.CreatedByUserId);
        Assert.Equal(ReportLinkKind.Report, stored.Kind);
    }

    /// <summary>
    /// The endpoint only accepts absolute https:// links. Its refusal reaches the agent verbatim
    /// rather than as a serialized blob, so the agent can tell what to fix.
    /// </summary>
    [Fact]
    public async Task CreateReport_IsRefused_WhenTheUrlIsNotHttps()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_report", "public-techniques", new
        {
            name = "Insecure link",
            description = "Points at plain http.",
            url = "http://contoso.example.com/adoption",
            kind = "Report",
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("HTTPS", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", McpTestJson.Text(result), StringComparison.Ordinal);
        Assert.False(await db.ReportLinks.AnyAsync());
    }

    [Fact]
    public async Task UpdateReport_ChangesOnlyWhatWasAsked()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var link = Report(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();

        // Captured now: `link` is tracked by this context, so the endpoint's own edit mutates it.
        var versionBefore = link.Version;

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_report", "public-techniques", new
        {
            id = link.Id.ToString(),
            description = "Weekly adoption by business group, refreshed each Monday.",
        }, user);

        Assert.False(result["isError"]!.GetValue<bool>());

        var stored = await db.ReportLinks.AsNoTracking().SingleAsync(r => r.Id == link.Id);
        Assert.Equal("Weekly adoption by business group, refreshed each Monday.", stored.Description);

        // A description edit must not move the entry or change what it is.
        Assert.Equal("Adoption dashboard", stored.Name);
        Assert.Equal("https://contoso.example.com/adoption", stored.Url);
        Assert.Equal(ReportLinkKind.Report, stored.Kind);
        Assert.NotEqual(versionBefore, stored.Version);
    }

    /// <summary>
    /// The conflict message this endpoint returns is an anonymous `{ "error": ... }`, not the typed
    /// shape the task and workflow endpoints use. It still has to reach the agent as a sentence.
    /// </summary>
    [Fact]
    public async Task UpdateReport_IsRefusedAsAConflict_WhenTheSuppliedVersionIsStale()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var link = Report(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("update_report", "public-techniques", new
        {
            id = link.Id.ToString(),
            name = "Based on a stale read",
            expectedVersion = Guid.NewGuid().ToString(),
        }, user);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("changed", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", McpTestJson.Text(result), StringComparison.Ordinal);

        Assert.Equal("Adoption dashboard", await db.ReportLinks.AsNoTracking()
            .Where(r => r.Id == link.Id).Select(r => r.Name).SingleAsync());
    }

    /// <summary>
    /// As for AI methods: the body is built from the tool's own read, so that read's version is
    /// what the write must be guarded by when the caller supplied none.
    /// </summary>
    [Fact]
    public async Task UpdateReport_GuardsWithItsOwnRead_WhenTheCallerSuppliedNoVersion()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var link = Report(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();

        var tools = McpInterleavedHost.Create(db, user, async () =>
        {
            var concurrent = await db.ReportLinks.SingleAsync(r => r.Id == link.Id);
            concurrent.Url = "https://contoso.example.com/adoption-v2";
            concurrent.Version = Guid.NewGuid();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

        var result = await tools.ExecuteAsync(
            "update_report",
            new JsonObject { ["collectionId"] = "public-techniques", ["id"] = link.Id.ToString(), ["name"] = "Adoption dashboard (v2)" },
            McpTestHost.Request("{}", user),
            CancellationToken.None);

        Assert.True(result.IsError);

        var stored = await db.ReportLinks.AsNoTracking().SingleAsync(r => r.Id == link.Id);
        Assert.Equal("Adoption dashboard", stored.Name);

        // The point of the guard: the moved URL was not overwritten by the one read beforehand.
        Assert.Equal("https://contoso.example.com/adoption-v2", stored.Url);
    }

    [Fact]
    public async Task DeleteReport_DescribesTheImpactFirst_AndOnlyDeletesOnceConfirmed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var link = Report(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var refused = await host.CallToolAsync("delete_report", "public-techniques", new { id = link.Id.ToString() }, user);

        Assert.True(refused["isError"]!.GetValue<bool>());
        Assert.Contains("Adoption dashboard", McpTestJson.Text(refused), StringComparison.Ordinal);
        Assert.True(await db.ReportLinks.AsNoTracking().AnyAsync(r => r.Id == link.Id));

        var confirmed = await host.CallToolAsync(
            "delete_report", "public-techniques", new { id = link.Id.ToString(), confirm = true }, user);

        Assert.False(confirmed["isError"]!.GetValue<bool>());
        Assert.False(await db.ReportLinks.AsNoTracking().AnyAsync(r => r.Id == link.Id));
    }

    [Fact]
    public async Task ReportWrites_AreRefused_WhenNoSignedInPersonCanBeNamed()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: true);

        var result = await host.CallToolAsync("create_report", "public-techniques", new
        {
            name = "Anonymous entry",
            description = "Should never be stored.",
            url = "https://contoso.example.com/nope",
            kind = "AiApp",
        });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("signed-in", McpTestJson.Text(result), StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.ReportLinks.AnyAsync());
    }

    [Fact]
    public async Task ReportWriteTools_AreRefused_WhenWritesAreDisabled()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User("Ada Lovelace");
        db.Users.Add(user);
        var link = Report(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();

        var host = McpTestHost.Create(db, user, writesEnabled: false);

        foreach (var tool in new[] { "create_report", "update_report", "delete_report" })
        {
            var response = await host.SendAsync("tools/call", new
            {
                name = tool,
                arguments = new { collectionId = "public-techniques", id = link.Id.ToString() },
            }, signedInAs: user);

            Assert.NotNull(response!["error"]);
        }

        Assert.Equal("Adoption dashboard", await db.ReportLinks.AsNoTracking()
            .Where(r => r.Id == link.Id).Select(r => r.Name).SingleAsync());
    }
}
