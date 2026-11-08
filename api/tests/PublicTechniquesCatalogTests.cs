using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Catalog;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class PublicTechniquesCatalogTests
{
    private const int TaskCount = 225;
    private const int WorkflowCount = 37;
    private const int MethodCount = 15;

    [Fact]
    public async Task ActualImporter_DryRunThenApplyThenRepeat_PreservesExactCountsAndScope()
    {
        await using var scope = await IsolatedCatalog.CreateAsync();
        await using (var json = CorpusStream())
        {
            var preview = await CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: true);
            Assert.Equal(TaskCount, preview.Tasks);
            Assert.Equal(WorkflowCount, preview.Workflows);
            Assert.Equal(MethodCount, preview.Methods);
        }
        scope.Db.ChangeTracker.Clear();
        await AssertEmptyCatalogAsync(scope.Db);
        Assert.Equal(1, await scope.Db.Users.CountAsync());
        Assert.Equal(0, await scope.Db.Set<CatalogImportReceipt>().CountAsync());

        await using (var json = CorpusStream())
        {
            var applied = await CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: false);
            Assert.Equal(TaskCount, applied.Tasks);
            Assert.Equal(WorkflowCount, applied.Workflows);
            Assert.Equal(MethodCount, applied.Methods);
        }
        await AssertImportedCountsAsync(scope.Db);

        await using (var json = CorpusStream())
        {
            var repeated = await CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: false);
            Assert.True(repeated.AlreadyImported);
            Assert.Equal(0, repeated.Tasks + repeated.Methods + repeated.Workflows);
        }
        await AssertImportedCountsAsync(scope.Db);
        Assert.Equal(1, await scope.Db.Set<CatalogImportReceipt>().CountAsync());

        Assert.All(await scope.Db.Tasks.ToListAsync(), task => Assert.Equal("public-techniques", task.CollectionId));
        Assert.All(await scope.Db.Workflows.ToListAsync(), workflow => Assert.Equal("public-techniques", workflow.CollectionId));
        Assert.All(await scope.Db.AiMethods.ToListAsync(), method => Assert.Equal("public-techniques", method.CollectionId));
        Assert.Equal(1, await scope.Db.Users.CountAsync());
        Assert.Empty(await scope.Db.Ratings.ToListAsync());
        Assert.Empty(await scope.Db.Comments.ToListAsync());
    }

    [Theory]
    [InlineData("unknown-task-reference")]
    [InlineData("unknown-method-reference")]
    [InlineData("unsupported-format-target")]
    [InlineData("context-only-target")]
    [InlineData("workflow-wrong-target")]
    [InlineData("overlong-name")]
    [InlineData("cross-collection-task")]
    public async Task ActualImporter_RejectsInvalidCorpusWithoutPartialWrites(string mutation)
    {
        await using var scope = await IsolatedCatalog.CreateAsync();
        var corpus = CorpusJson();
        var task = corpus["tasks"]![0]!;
        var workflow = corpus["workflows"]!.AsArray()[^1]!;
        switch (mutation)
        {
            case "unknown-task-reference":
                workflow["steps"]![0]!["taskId"] = Guid.NewGuid().ToString();
                break;
            case "unknown-method-reference":
                task["methodId"] = Guid.NewGuid().ToString();
                break;
            case "unsupported-format-target":
                task["format"] = "anthropic.plugin";
                break;
            case "context-only-target":
                task["targetIds"] = new JsonArray("microsoft.word");
                break;
            case "workflow-wrong-target":
                workflow["steps"]![0]!["targetId"] = "microsoft.scout.desktop";
                break;
            case "overlong-name":
                task["name"] = new string('x', 201);
                break;
            case "cross-collection-task":
                task["collectionId"] = "curated-tools";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await AssertRejectedAsync(scope, corpus);
        await AssertEmptyCatalogAsync(scope.Db);
        Assert.Equal(1, await scope.Db.Users.CountAsync());
    }

    [Fact]
    public async Task ActualImporter_RequiresExistingActorWithoutManufacturingAnIdentity()
    {
        await using var scope = await IsolatedCatalog.CreateAsync();
        await using var json = CorpusStream();
        var absentActor = new User
        {
            Id = Guid.NewGuid(), EntraTenantId = Guid.NewGuid().ToString(),
            EntraObjectId = Guid.NewGuid().ToString(), DisplayName = "Absent test principal",
        };
        var error = await Record.ExceptionAsync(() =>
            CatalogImportService.ImportAsync(scope.Db, json, absentActor, dryRun: false));
        AssertValidationRejection(error);
        await AssertEmptyCatalogAsync(scope.Db);
        Assert.Equal(1, await scope.Db.Users.CountAsync());
    }

    [Fact]
    public async Task ActualImporter_RollsBackAfterRealDatabaseWritesFailMidImport()
    {
        var failure = new MidWriteFailure();
        await using var scope = await IsolatedCatalog.CreateAsync(failure);
        failure.Enabled = true;
        await using var json = CorpusStream();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: false));
        Assert.Equal("Injected isolated import failure after intermediate writes.",
            Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.True(failure.InsertCount >= 20);
        scope.Db.ChangeTracker.Clear();
        await AssertEmptyCatalogAsync(scope.Db);
        Assert.Equal(0, await scope.Db.Set<CatalogImportReceipt>().CountAsync());
        Assert.Equal(1, await scope.Db.Users.CountAsync());
    }

    [Fact]
    public async Task ActualImporter_NeverOverwritesAnExistingForeignTask()
    {
        await using var scope = await IsolatedCatalog.CreateAsync();
        var corpus = CorpusJson();
        var id = Guid.Parse(corpus["tasks"]![0]!["id"]!.GetValue<string>());
        scope.Db.Tasks.Add(new TaskItem
        {
            Id = id, Name = "Existing user content", Description = "Must not be replaced by a dataset import.",
            TaskType = "prompt", TargetIds = ["microsoft.copilot.chat"], Format = "text.plain",
            CollectionId = "public-techniques", InlineContent = "Existing user-authored instructions.",
            CreatedByUserId = scope.Actor.Id, LastEditedByUserId = scope.Actor.Id,
        });
        await scope.Db.SaveChangesAsync();

        await AssertRejectedAsync(scope, corpus);
        scope.Db.ChangeTracker.Clear();
        var retained = await scope.Db.Tasks.SingleAsync();
        Assert.Equal("Existing user content", retained.Name);
        Assert.Equal("Existing user-authored instructions.", retained.InlineContent);
        Assert.Empty(await scope.Db.Workflows.ToListAsync());
        Assert.Empty(await scope.Db.AiMethods.ToListAsync());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, TaskCount)]
    public async Task ActualPublicApi_OnlyReadsReviewedScopeAndNeverAllowsAnonymousPublication(bool publishReviewed, int visibleCount)
    {
        await using var scope = await IsolatedCatalog.CreateAsync();
        await using (var json = CorpusStream())
            await CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: false, publishReviewed: publishReviewed);
        var function = new WorkflowGallery.Api.Functions.Tasks(scope.Db, new FakeCurrentUserService(null),
            new FakeBlobStorageService(), new FileValidationService());
        var publicPolicy = new PublicDemoPolicy(true);
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
        {
            ["collectionId"] = "public-techniques",
        });
        request.Method = HttpMethods.Get;
        request.Path = "/api/tasks";
        publicPolicy.Stamp(request.HttpContext);
        var response = Assert.IsType<OkObjectResult>(await function.List(request, CancellationToken.None));
        Assert.Equal(visibleCount, Assert.IsType<PagedResultDto<TaskSummaryDto>>(response.Value).TotalCount);

        var publicTaskId = Guid.Parse(CorpusJson()["tasks"]![0]!["id"]!.GetValue<string>());
        var sameCollectionDetail = await function.GetById(request, publicTaskId, CancellationToken.None);
        if (publishReviewed)
        {
            var detail = Assert.IsType<OkObjectResult>(sameCollectionDetail);
            Assert.Equal(publicTaskId, Assert.IsType<TaskDetailDto>(detail.Value).Id);
        }
        else
        {
            Assert.IsType<NotFoundResult>(sameCollectionDetail);
        }

        request.QueryString = new QueryString("?collectionId=curated-tools");
        var otherCollection = Assert.IsType<OkObjectResult>(await function.List(request, CancellationToken.None));
        Assert.Equal(0, Assert.IsType<PagedResultDto<TaskSummaryDto>>(otherCollection.Value).TotalCount);
        Assert.IsType<NotFoundResult>(await function.GetById(request, publicTaskId, CancellationToken.None));

        var write = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>());
        write.QueryString = new QueryString("?collectionId=public-techniques");
        write.Method = HttpMethods.Post;
        write.Path = "/api/tasks";
        publicPolicy.Stamp(write.HttpContext);
        var denied = Assert.IsType<ObjectResult>(await function.Create(write, CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
        scope.Db.CollectionId = "public-techniques";
        scope.Db.PublicOnly = false;
        await AssertImportedCountsAsync(scope.Db);
        Assert.Equal(1, await scope.Db.Users.CountAsync());
    }

    [Fact]
    public void InlineExamples_RespectActualApplicationFileRulesWithoutUploadingAnything()
    {
        var fileRules = new FileValidationService();
        var corpus = CorpusJson();
        foreach (var task in corpus["tasks"]!.AsArray())
        {
            var content = task!["artifact"]!["content"]?.GetValue<string>();
            if (content is null) continue;
            var format = task["format"]!.GetValue<string>();
            var extension = format is "anthropic.plugin" or "mcp.tool-json" ? ".json" : ".md";
            var filename = task["fixtureId"]!.GetValue<string>() + extension;
            var result = fileRules.Validate(filename, Encoding.UTF8.GetByteCount(content));
            Assert.True(result.IsValid, $"{filename}: {result.ErrorMessage}");
        }
    }

    public static IEnumerable<object[]> GoldenCases() =>
            CorpusJson()["scenarios"]!.AsArray().Select(scenario => new object[]
            {
                scenario!["id"]!.GetValue<string>(),
            });

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void GoldenIntent_ActualCandidateSelectorFindsExpectedCompatibleTask(string scenarioId)
        {
            var corpus = CorpusJson();
            var scenario = corpus["scenarios"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == scenarioId)!;
            var snapshots = corpus["tasks"]!.AsArray().Select(task => new CatalogTaskSnapshot(
                Guid.Parse(task!["id"]!.GetValue<string>()),
                task["name"]!.GetValue<string>(),
                task["description"]!.GetValue<string>(),
                task["taskType"]!.GetValue<string>(),
                task["targetIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray(),
                task["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ToArray(),
                task["format"]?.GetValue<string>())).ToArray();
            var query = scenario["query"]!.GetValue<string>();
            var selected = CatalogCandidateSelector.Select(query, snapshots);
            var expectedIds = scenario["expectedTaskIds"]!.AsArray()
                .Select(id => Guid.Parse(id!.GetValue<string>())).ToArray();
            foreach (var expectedId in expectedIds)
                Assert.Contains(selected, task => task.Id == expectedId);
            var publicSelected = CatalogCandidateSelector.SelectPublic(query, snapshots, 12000);
            foreach (var expectedId in expectedIds)
                Assert.Contains(publicSelected, task => task.Id == expectedId);
            Assert.True(Encoding.UTF8.GetByteCount(AzureAiComposerService.CatalogJson(publicSelected)) <= 12000);
            Assert.True(selected.Count <= CatalogCandidateSelector.MaximumCandidates);
            Assert.True(JsonSerializer.Serialize(selected).Length <= CatalogCandidateSelector.MaximumContextCharacters);
            Assert.Equal(selected.Select(task => task.Id),
                CatalogCandidateSelector.Select(query, snapshots.Reverse()).Select(task => task.Id));

            var targets = scenario["targetIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray();
            var format = scenario["format"]?.GetValue<string>();
            var kind = scenario["taskType"]!.GetValue<string>();
            var eligible = snapshots.Where(task => task.TaskType == kind && task.Format == format
                && targets.All(target => task.TargetIds.Contains(target)));
            var constrained = CatalogCandidateSelector.Select(query, eligible);
            foreach (var expectedId in expectedIds)
                Assert.Contains(constrained, task => task.Id == expectedId);
            Assert.All(constrained, task =>
            {
                Assert.Equal(kind, task.TaskType);
                Assert.Equal(format, task.Format);
                Assert.All(targets, target => Assert.Contains(target, task.TargetIds));
            });
    }

    private static async Task AssertRejectedAsync(IsolatedCatalog scope, JsonObject corpus)
    {
        await using var json = new MemoryStream(Encoding.UTF8.GetBytes(corpus.ToJsonString()));
        var error = await Record.ExceptionAsync(() =>
            CatalogImportService.ImportAsync(scope.Db, json, scope.Actor, dryRun: false));
        AssertValidationRejection(error);
        scope.Db.ChangeTracker.Clear();
    }

    private static void AssertValidationRejection(Exception? error)
    {
        Assert.NotNull(error);
        Assert.True(error is ArgumentException or InvalidOperationException,
            $"Expected explicit validation rejection, not {error.GetType().Name}: {error.Message}");
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static async Task AssertEmptyCatalogAsync(AppDbContext db)
    {
        Assert.Equal(0, await db.Tasks.CountAsync());
        Assert.Equal(0, await db.Workflows.CountAsync());
        Assert.Equal(0, await db.WorkflowSteps.CountAsync());
        Assert.Equal(0, await db.AiMethods.CountAsync());
    }

    private static async Task AssertImportedCountsAsync(AppDbContext db)
    {
        Assert.Equal(TaskCount, await db.Tasks.CountAsync());
        Assert.Equal(WorkflowCount, await db.Workflows.CountAsync());
        Assert.Equal(MethodCount, await db.AiMethods.CountAsync());
        Assert.Equal(CorpusJson()["workflows"]!.AsArray().Sum(workflow => workflow!["steps"]!.AsArray().Count),
            await db.WorkflowSteps.CountAsync());
    }

    private static JsonObject CorpusJson() => JsonNode.Parse(File.ReadAllText(CorpusPath()))!.AsObject();
    private static FileStream CorpusStream() => File.OpenRead(CorpusPath());

    private static string CorpusPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "datasets", "public-techniques", "catalog.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("The checked-in public techniques corpus must be available to integration tests.");
    }

    private sealed class IsolatedCatalog : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }
        public User Actor { get; }

        private IsolatedCatalog(SqliteConnection connection, AppDbContext db, User actor)
        {
            _connection = connection;
            Db = db;
            Actor = actor;
        }

        public static async Task<IsolatedCatalog> CreateAsync(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new AppDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            // Test-only principal in an isolated database, never part of the public seed.
            var actor = new User
            {
                Id = Guid.NewGuid(), EntraTenantId = Guid.NewGuid().ToString(), EntraObjectId = Guid.NewGuid().ToString(),
                DisplayName = "Isolated import test principal",
            };
            db.Users.Add(actor);
            await db.SaveChangesAsync();
            return new IsolatedCatalog(connection, db, actor);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class MidWriteFailure : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int InsertCount { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                && ++InsertCount == 20)
                throw new InvalidOperationException("Injected isolated import failure after intermediate writes.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
