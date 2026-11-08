using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Catalog;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class CatalogStorageTests
{
    [Fact]
    public async Task PrintedSchema_IncludesExactlyOneMarkerInsert_AndMatchesInitialization()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var script = CatalogSchema.GenerateCreateScript(db);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, "INSERT INTO \"CatalogSchemaMarker\""));
        Assert.Contains($"VALUES (1, '{CatalogSchema.CurrentVersion}')", script);
        Assert.DoesNotContain("IDENTITY_INSERT", script);
        await db.Database.ExecuteSqlRawAsync(script);
        await CatalogSchema.VerifyAsync(db);
        var marker = Assert.Single(await db.Set<CatalogSchemaMarker>().ToListAsync());
        Assert.Equal(1, marker.Id);
        Assert.Equal(CatalogSchema.CurrentVersion, marker.Version);
        using var sqlServer = new AppDbContextFactory().CreateDbContext(Array.Empty<string>());
        var sql = CatalogSchema.GenerateCreateScript(sqlServer);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"INSERT INTO \[CatalogSchemaMarker\]"));
        Assert.Contains($"VALUES (1, N'{CatalogSchema.CurrentVersion}')", sql);
        Assert.DoesNotContain("IDENTITY_INSERT", sql);
    }

    [Fact]
    public async Task Verify_RefusesMarkerOnlySchema_WithoutCreatingAnyMissingTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE CatalogSchemaMarker (Id INTEGER PRIMARY KEY, Version TEXT NOT NULL)");
        db.Set<CatalogSchemaMarker>().Add(new() { Id = 1, Version = CatalogSchema.CurrentVersion });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogSchema.VerifyAsync(db));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Import_RejectsMissingForeignCollectionsAndOversizedStreamsBeforeWriting()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        foreach (var collection in new[] { "curated-tools", "" })
        {
            var envelope = JsonNode.Parse(Dataset().GetRawText())!;
            var task = envelope["tasks"]![0]!.AsObject();
            if (collection.Length == 0) task.Remove("collectionId");
            else task["collectionId"] = collection;
            using var json = JsonDocument.Parse(envelope.ToJsonString());
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, json.RootElement, user));
            Assert.Contains("collectionId", error.Message);
        }
        await using var oversized = new MemoryStream(new byte[CatalogImportService.MaximumDatasetBytes + 1]);
        var lengthError = await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, oversized, user, dryRun: false));
        Assert.Contains("5 MiB", lengthError.Message);
        Assert.Empty(await db.Tasks.ToListAsync());
        Assert.Empty(await db.AiMethods.ToListAsync());
        Assert.Empty(await db.Workflows.ToListAsync());
        Assert.Empty(await db.Set<CatalogImportReceipt>().ToListAsync());
    }

    [Fact]
    public async Task Initialize_CreatesOnlyEmptyDatabase_AndVerifyIsReadOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogSchema.VerifyAsync(db));
        await CatalogSchema.InitializeAsync(db);
        await CatalogSchema.VerifyAsync(db);
        Assert.Equal(CatalogSchema.CurrentVersion, (await db.Set<CatalogSchemaMarker>().SingleAsync()).Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogSchema.InitializeAsync(db));
        await CatalogSchema.VerifyAsync(db);
    }

    [Fact]
    public async Task Initialize_RefusesLegacyDatabaseWithoutChangingItsTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE LegacyData (Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO LegacyData VALUES (1, 'keep')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogSchema.InitializeAsync(db));
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM LegacyData WHERE Id = 1";
        Assert.Equal("keep", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Import_UsesSharedValidation_DryRunRollsBack_AndDatasetIdIsIdempotent()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var envelope = Dataset();
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(envelope.GetRawText()));
        var dryRun = await CatalogImportService.ImportAsync(db, stream, user, dryRun: true);
        Assert.True(dryRun.DryRun);
        Assert.Equal(2, dryRun.Tasks);
        Assert.Empty(await db.Tasks.ToListAsync());
        Assert.Empty(await db.AiMethods.ToListAsync());
        Assert.Empty(await db.Workflows.ToListAsync());
        Assert.Empty(await db.Set<CatalogImportReceipt>().ToListAsync());
        Assert.Single(await db.Users.ToListAsync());
        var result = await CatalogImportService.ImportAsync(db, envelope, user);
        Assert.False(result.AlreadyImported);
        Assert.Equal(2, await db.Tasks.CountAsync());
        Assert.Equal(1, await db.Workflows.CountAsync());
        Assert.Equal(1, await db.AiMethods.CountAsync());
        Assert.All(await db.Tasks.ToListAsync(), t =>
        {
            Assert.Equal(user.Id, t.CreatedByUserId);
            Assert.False(t.IsPublished);
            Assert.Null(CatalogRegistry.Default.ValidateTask(t.TaskType, t.TargetIds, t.Format));
        });
        Assert.True((await CatalogImportService.ImportAsync(db, envelope, user)).AlreadyImported);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, Dataset("different-dataset"), user));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, Dataset(invalidTarget: true), user));
        Assert.Equal(2, await db.Tasks.CountAsync());
        var edited = await db.Tasks.FirstAsync();
        edited.Description = "Edited after import";
        await db.SaveChangesAsync();
        var editError = await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, envelope, user));
        Assert.Contains("edited", editError.Message);
    }

    [Fact]
    public async Task Import_InvalidTargetsAndUnknownPrincipalNeverWritePartialCatalog()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, Dataset(), User()));
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogImportService.ImportAsync(db, Dataset(invalidTarget: true), user));
        Assert.Empty(await db.Tasks.ToListAsync());
        Assert.Empty(await db.AiMethods.ToListAsync());
        Assert.Empty(await db.Workflows.ToListAsync());
        Assert.Empty(await db.Set<CatalogImportReceipt>().ToListAsync());
    }

    private static JsonElement Dataset(string datasetId = "test-dataset", bool invalidTarget = false)
    {
        var methodId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var task1 = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var task2 = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var target = "microsoft.copilot.m365";
        return JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, datasetId, collectionId = "public-techniques", checkedAt = "2026-11-05",
            methods = new[] { new { id = methodId, collectionId = "public-techniques", title = "Compare", summary = "Compare evidence", approach = "Review the evidence." } },
            tasks = new[]
            {
                new { id = task1, collectionId = "public-techniques", name = "Summarize", description = "Summarize evidence", taskType = "prompt",
                    targetIds = new[] { target }, format = "text.plain", inlineContent = "Summarize supplied evidence.", methodId },
                new { id = task2, collectionId = "public-techniques", name = "Verify", description = "Verify summary", taskType = "prompt",
                    targetIds = new[] { target }, format = "text.plain", inlineContent = "Verify the supplied summary.", methodId }
            },
            workflows = new[]
            {
                new { id = Guid.Parse("30000000-0000-0000-0000-000000000001"), collectionId = "public-techniques", name = "Reviewed summary",
                    problemStatement = "Need accurate summary", desiredOutcome = "Verified summary", methodId,
                    steps = new[]
                    {
                        new { taskId = task1, targetId = target, isManualStep = false },
                        new { taskId = task2, targetId = invalidTarget ? "microsoft.word" : target, isManualStep = false }
                    }
                }
            }
        });
    }
}
