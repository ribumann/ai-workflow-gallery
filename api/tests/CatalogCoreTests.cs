using System.Text.Json;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class CatalogCoreTests
{
    private const string M365 = "microsoft.copilot.m365";
    private const string VsCode = "github.copilot.vscode";

    [Fact]
    public async Task TaskList_SelectsOnlyMetadataAndContentPresence_NotArtifactBodiesOrBlobPaths()
    {
        var capture = new ReadCapture();
        await using var store = await RelationalTestDb.Create(capture);
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        db.Tasks.Add(new()
        {
            Id = Guid.NewGuid(), Name = "Large prompt", Description = "Metadata only", TaskType = "prompt",
            TargetIds = new() { M365 }, Tags = new() { "large" }, InlineContent = new string('x', 100000),
            AttachedFileBlobPath = "private-storage-object-key", CreatedByUserId = user.Id, LastEditedByUserId = user.Id
        });
        await db.SaveChangesAsync();
        capture.Commands.Clear();
        var response = Assert.IsType<OkObjectResult>(await TaskApi(db).List(Query(("q", "large"), ("targetId", M365)), Ct));
        var item = Assert.Single(Assert.IsType<PagedResultDto<TaskSummaryDto>>(response.Value).Items);
        Assert.True(item.HasInlineContent);
        Assert.True(item.HasAttachedFile);
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("\"InlineContent\" IS NOT NULL", sql);
        Assert.Contains("\"AttachedFileBlobPath\" IS NOT NULL", sql);
        Assert.DoesNotContain("\"t\".\"InlineContent\",", sql);
        Assert.DoesNotContain("\"t\".\"AttachedFileBlobPath\",", sql);
        Assert.DoesNotContain("\"AttachedFileName\"", sql);
        Assert.DoesNotContain("\"AttachedFileSizeBytes\"", sql);
        Assert.DoesNotContain("private-storage-object-key", JsonSerializer.Serialize(response.Value));
        Assert.DoesNotContain(new string('x', 100), JsonSerializer.Serialize(response.Value));
    }

    private sealed class ReadCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("prompt", M365, null, true)]
    [InlineData("prompt", M365, "text.plain", true)]
    [InlineData("skill", VsCode, "agentskills.skill-md", true)]
    [InlineData("skill", M365, null, false)]
    [InlineData("prompt", "microsoft.word", null, false)]
    [InlineData("prompt", "invented.target", null, false)]
    [InlineData("Prompt", M365, null, false)]
    [InlineData("prompt", M365, "agentskills.skill-md", false)]
    public void Registry_UsesOnlyExplicitProfiles(string kind, string target, string? format, bool valid) =>
        Assert.Equal(valid, CatalogRegistry.Default.ValidateTask(kind, new[] { target }, format) is null);

    [Fact]
    public void Registry_RejectsMissingDuplicateAndOversizedTargets()
    {
        var registry = CatalogRegistry.Default;
        Assert.NotNull(registry.ValidateTask("prompt", null, null));
        Assert.NotNull(registry.ValidateTask("prompt", Array.Empty<string>(), null));
        Assert.NotNull(registry.ValidateTask("prompt", new[] { M365, M365 }, null));
        Assert.NotNull(registry.ValidateTask("prompt", Enumerable.Repeat(M365, 17).ToArray(), null));
        Assert.NotNull(registry.ValidateTask("prompt", new[] { M365 }, "invented.format"));
        Assert.Null(registry.ValidateTask("prompt", new[] { M365, VsCode }, "text.plain"));
    }

    [Fact]
    public void Registry_RejectsInvalidEvidenceAndReferences()
    {
        var original = CatalogRegistry.Default.Definition;
        var target = original.Targets.First(t => t.Profiles.Count > 0);
        foreach (var profile in new[]
        {
            target.Profiles[0] with { SourceUrl = "http://example.com" },
            target.Profiles[0] with { ReviewedOn = "2026-02-30" },
            target.Profiles[0] with { Kind = "invented" },
            target.Profiles[0] with { Formats = new() { "invented" } },
        })
            Assert.Throws<InvalidOperationException>(() => new CatalogRegistry(original with
                { Targets = new() { target with { Profiles = new() { profile } } } }));
        Assert.Throws<InvalidOperationException>(() => new CatalogRegistry(original with
            { Kinds = original.Kinds.Concat(new[] { original.Kinds[0] }).ToList() }));
    }

    [Fact]
    public void CandidateSelection_RanksRelevantLateEntriesAndBoundsSerializedMetadata()
    {
        var catalog = Enumerable.Range(1, 240).Select(i => new CatalogTaskSnapshot(
            Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"), $"Routine {i}", "Generic content",
            "prompt", new[] { M365 }, Array.Empty<string>())).ToList();
        var relevant = catalog[^1] with { Name = "Plan a database recovery", Description = "Restore backup and verify recovery", Tags = new[] { "recovery" } };
        catalog[^1] = relevant;
        catalog.Add(relevant with { Id = Guid.NewGuid(), TaskType = "skill", Name = "Recovery recovery", TargetIds = new[] { "microsoft.word" } });
        var selected = CatalogCandidateSelector.Select("Plan database backup recovery", catalog);
        Assert.Equal(relevant.Id, selected[0].Id);
        Assert.Equal(CatalogCandidateSelector.MaximumCandidates, selected.Count);
        Assert.All(selected, s => Assert.Null(CatalogRegistry.Default.ValidateTask(s.TaskType, s.TargetIds, s.Format)));
        Assert.True(JsonSerializer.Serialize(selected).Length <= CatalogCandidateSelector.MaximumContextCharacters);
        Assert.Equal(selected.Select(x => x.Id), CatalogCandidateSelector.Select("Plan database backup recovery", catalog.AsEnumerable().Reverse()).Select(x => x.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invented")]
    [InlineData(VsCode)]
    public void Composer_UnknownMissingOrUnselectedTargetBecomesGap(string? target)
    {
        var task = new CatalogTaskSnapshot(Guid.NewGuid(), "Task", "Description", "prompt", new[] { M365 }, Array.Empty<string>(), "text.plain");
        var raw = new AzureAiComposerService.RawProposal("Workflow", "Problem", "Outcome",
            new() { new(1, task.Id.ToString(), null, target) }, new());
        var proposal = AzureAiComposerService.ResolveAgainstCatalog(raw, new[] { task });
        Assert.Empty(proposal.Steps);
        Assert.Single(proposal.Gaps);
    }

    [Fact]
    public async Task TaskRoutes_RequireScopeAndRejectUnknownFilters()
    {
        await using var db = TestDbContextFactory.Create();
        var api = TaskApi(db);
        Assert.IsType<BadRequestObjectResult>(await api.List(new DefaultHttpContext().Request, Ct));
        Assert.IsType<BadRequestObjectResult>(await api.List(Query(("type", "Prompt")), Ct));
        Assert.IsType<BadRequestObjectResult>(await api.List(Query(("targetId", "unknown")), Ct));
        Assert.IsType<BadRequestObjectResult>(await api.List(Query(("collectionId", "unknown")), Ct));
    }

    [Fact]
    public async Task TaskWrite_ReadsBackMetadataAndRejectsDuplicateTargetsWithoutMutation()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var api = TaskApi(db, user);
        var created = Assert.IsType<CreatedResult>(await api.Create(TaskBody(), Ct));
        Assert.Equal(201, created.StatusCode);
        var task = Assert.IsType<TaskDetailDto>(created.Value);
        Assert.Equal(new[] { M365, VsCode }, task.TargetIds);
        Assert.Equal("text.plain", task.Format);
        Assert.Equal("Setup", task.Setup);
        Assert.Equal("Prerequisites", task.Prerequisites);
        var listed = Assert.IsType<PagedResultDto<TaskSummaryDto>>(Assert.IsType<OkObjectResult>(await api.List(Query(("targetId", VsCode)), Ct)).Value);
        Assert.Equal("Setup", Assert.Single(listed.Items).Setup);
        var invalid = TaskBody(new[] { M365, M365 });
        Assert.IsType<BadRequestObjectResult>(await api.Update(invalid, task.Id, Ct));
        Assert.Equal(1, await db.Tasks.CountAsync());
        Assert.Equal(new[] { M365, VsCode }, (await db.Tasks.SingleAsync()).TargetIds);
    }

    [Fact]
    public async Task Workflow_EnforcesActualTargetAndPreservesDeletionSnapshotAndManualNulls()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var taskApi = TaskApi(db, user);
        var task = Assert.IsType<TaskDetailDto>(Assert.IsType<CreatedResult>(await taskApi.Create(TaskBody(), Ct)).Value);
        var workflows = new Workflows(db, new FakeCurrentUserService(user));
        var invalid = WorkflowBody(task.Id, "microsoft.copilot.chat");
        Assert.IsType<BadRequestObjectResult>(await workflows.Create(invalid, Ct));
        var created = Assert.IsType<ObjectResult>(await workflows.Create(WorkflowBody(task.Id, VsCode), Ct));
        var workflow = Assert.IsType<WorkflowDetailDto>(created.Value);
        Assert.Equal(new[] { M365, VsCode }, workflow.Steps[0].TaskTargetIds);
        Assert.Null(workflow.Steps[1].TaskType);
        Assert.Null(workflow.Steps[1].TargetId);
        Assert.Empty(workflow.Steps[1].TaskTargetIds);
        Assert.IsType<BadRequestObjectResult>(await taskApi.Update(TaskBody(new[] { M365 }), task.Id, Ct));
        Assert.IsType<NoContentResult>(await taskApi.Delete(Query(("confirm", "true")), task.Id, Ct));
        var after = Assert.IsType<WorkflowDetailDto>(Assert.IsType<OkObjectResult>(await workflows.GetById(Query(), workflow.Id, Ct)).Value);
        Assert.Equal("Reusable prompt", after.Steps[0].TaskName);
        Assert.Equal("prompt", after.Steps[0].TaskType);
        Assert.Equal(VsCode, after.Steps[0].TargetId);
        Assert.Empty(after.Steps[0].TaskTargetIds);
        Assert.False(after.Steps[0].TaskAvailable);
    }

    [Fact]
    public async Task Collections_IsolateAllCatalogEntitiesAndRejectCrossCollectionLinks()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var publicTask = Assert.IsType<TaskDetailDto>(Assert.IsType<CreatedResult>(await TaskApi(db, user).Create(TaskBody(), Ct)).Value);
        var curatedRequest = TaskBody();
        curatedRequest.QueryString = new("?collectionId=curated-tools");
        var curatedTask = Assert.IsType<TaskDetailDto>(Assert.IsType<CreatedResult>(await TaskApi(db, user).Create(curatedRequest, Ct)).Value);
        Assert.Equal("curated-tools", (await db.Tasks.SingleAsync()).CollectionId);
        Assert.IsType<NotFoundResult>(await TaskApi(db, user).GetById(Query(("collectionId", "curated-tools")), publicTask.Id, Ct));
        Assert.IsType<NotFoundResult>(await new Methods(db, new FakeCurrentUserService(user)).GetById(Query(("collectionId", "curated-tools")), method.Id, Ct));
        var crossMethod = TaskBody(methodId: method.Id);
        crossMethod.QueryString = new("?collectionId=curated-tools");
        Assert.IsType<BadRequestObjectResult>(await TaskApi(db, user).Create(crossMethod, Ct));
        var workflowRequest = WorkflowBody(publicTask.Id, M365);
        workflowRequest.QueryString = new("?collectionId=curated-tools");
        Assert.IsType<BadRequestObjectResult>(await new Workflows(db, new FakeCurrentUserService(user)).Create(workflowRequest, Ct));
        var publicPage = Assert.IsType<PagedResultDto<TaskSummaryDto>>(Assert.IsType<OkObjectResult>(await TaskApi(db, user).List(Query(), Ct)).Value);
        Assert.Equal(publicTask.Id, Assert.Single(publicPage.Items).Id);
        var curatedPage = Assert.IsType<PagedResultDto<TaskSummaryDto>>(Assert.IsType<OkObjectResult>(await TaskApi(db, user).List(Query(("collectionId", "curated-tools")), Ct)).Value);
        Assert.Equal(curatedTask.Id, Assert.Single(curatedPage.Items).Id);
        Assert.Equal(2, await db.Tasks.IgnoreQueryFilters().CountAsync());
    }

    private static Tasks TaskApi(AppDbContext db, User? user = null) =>
        new(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());
    private static HttpRequest TaskBody(string[]? targets = null, Guid? methodId = null) =>
        TestHttpRequestFactory.CreateWithForm(new()
        {
            ["metadata"] = new StringValues(JsonSerializer.Serialize(new
            {
                name = "Reusable prompt", description = "A prompt", taskType = "prompt",
                targetIds = targets ?? new[] { M365, VsCode }, format = "text.plain", setup = "Setup", prerequisites = "Prerequisites",
                inlineContent = "Summarize supplied information.", methodId
            }))
        });
    private static HttpRequest WorkflowBody(Guid taskId, string targetId) =>
        TestHttpRequestFactory.CreateWithJsonBody(JsonSerializer.Serialize(new
        {
            name = "Review workflow", problemStatement = "Need reviewed summary", desiredOutcome = "Reviewed output",
            steps = new object[]
            {
                new { taskId, targetId },
                new { isManualStep = true, manualStepTitle = "Review", targetId = (string?)null }
            }
        }));
}
