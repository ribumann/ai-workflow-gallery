using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class MethodAssociationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateSelect_UpdatePreserveSelectClear_AndReadPersistAssociation(bool workflow)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var owner = User();
        var first = Method(owner, "First");
        var second = Method(owner, "Second");
        db.AiMethods.AddRange(first, second);
        await db.SaveChangesAsync();
        var response = Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, workflow, JsonSerializer.Serialize(first.Id)));
        Assert.Equal(201, response.StatusCode);
        var id = DetailId(response);
        AssertAssociation(response, first);
        var initialSteps = workflow ? Assert.IsType<WorkflowDetailDto>(response.Value).Steps : null;

        var preserved = Assert.IsType<OkObjectResult>(await Update(db, owner, workflow, id, name: "Renamed"));
        AssertAssociation(preserved, first);
        if (workflow) Assert.Equal(JsonSerializer.Serialize(initialSteps), JsonSerializer.Serialize(Assert.IsType<WorkflowDetailDto>(preserved.Value).Steps));
        var selected = Assert.IsType<OkObjectResult>(await Update(db, owner, workflow, id, JsonSerializer.Serialize(second.Id)));
        AssertAssociation(selected, second);
        AssertAssociation(Assert.IsType<OkObjectResult>(await Get(db, owner, workflow, id)), second);
        var cleared = Assert.IsType<OkObjectResult>(await Update(db, owner, workflow, id, "null"));
        AssertAssociation(cleared, null);
        AssertAssociation(Assert.IsType<OkObjectResult>(await Get(db, owner, workflow, id)), null);
        if (workflow) Assert.Equal(JsonSerializer.Serialize(initialSteps), JsonSerializer.Serialize(Assert.IsType<WorkflowDetailDto>(cleared.Value).Steps));
        Assert.Equal(2, await db.AiMethods.CountAsync());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "null")]
    [InlineData(true, null)]
    [InlineData(true, "null")]
    public async Task LegacyCreate_OptionalMethodRemainsNull(bool workflow, string? value)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var result = Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow, value));
        Assert.Equal(201, result.StatusCode);
        AssertAssociation(result, null);
        AssertAssociation(Assert.IsType<OkObjectResult>(await Get(db, user, workflow, DetailId(result))), null);
    }

    [Theory]
    [InlineData(false, "\"\"")]
    [InlineData(true, "\"\"")]
    [InlineData(false, "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData(true, "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData(false, "\"not-a-guid\"")]
    [InlineData(true, "\"not-a-guid\"")]
    [InlineData(false, "7")]
    [InlineData(true, "7")]
    [InlineData(false, "false")]
    [InlineData(true, "false")]
    [InlineData(false, "[]")]
    [InlineData(true, "[]")]
    [InlineData(false, "{}")]
    [InlineData(true, "{}")]
    [InlineData(false, "\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData(true, "\"11111111-1111-1111-1111-111111111111\"")]
    public async Task InvalidSelection_RejectsCreateAndUpdate_WithoutChangingParentOrAnyStep(bool workflow, string value)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var created = Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow, JsonSerializer.Serialize(method.Id)));
        var id = DetailId(created);
        var before = await Snapshot(db, workflow, id);
        var blobs = new FakeBlobStorageService();
        MethodsFunctionTests.AssertError(await Update(db, user, workflow, id, value, name: "Must not save", blobs: blobs, file: true));
        Assert.Equal(before, await Snapshot(db, workflow, id));
        MethodsFunctionTests.AssertError(await Create(db, user, workflow, value, blobs: blobs, file: true));
        Assert.Equal(1, workflow ? await db.Workflows.CountAsync() : await db.Tasks.CountAsync());
        Assert.Empty(blobs.UploadedFileNames);
        Assert.Empty(blobs.DeletedBlobPaths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictDetail_IncludesCurrentAssociation_AndDoesNotChangeSavedContent(bool workflow)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var created = Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow, JsonSerializer.Serialize(method.Id)));
        var id = DetailId(created);
        var before = await Snapshot(db, workflow, id);
        var result = Assert.IsType<ConflictObjectResult>(await Update(db, user, workflow, id, "null", DateTime.UtcNow.AddDays(-2)));
        AssertAssociation(result, method);
        Assert.Equal(before, await Snapshot(db, workflow, id));
    }

    [Fact]
    public void OldClientConstructors_SerializeWithoutMethod_ExplicitNullKeepsPresence()
    {
        var task = new TaskWriteDto("Task", "Description", "prompt", ["microsoft.copilot.m365"], [], null, "Content", null, null, null);
        var create = new CreateWorkflowRequestDto("Workflow", "Problem", "Outcome", []);
        var update = new UpdateWorkflowRequestDto("Workflow", "Problem", "Outcome", [], null);
        foreach (var dto in new object[] { task, create, update })
            Assert.DoesNotContain("methodId", JsonSerializer.Serialize(dto, JsonOptions));
        Assert.Equal(JsonValueKind.Undefined, JsonSerializer.Deserialize<TaskWriteDto>(
            JsonSerializer.Serialize(task, JsonOptions), JsonOptions)!.MethodId.ValueKind);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.Deserialize<TaskWriteDto>("{\"methodId\":null}", JsonOptions)!.MethodId.ValueKind);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.Deserialize<CreateWorkflowRequestDto>("{\"methodId\":null}", JsonOptions)!.MethodId.ValueKind);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.Deserialize<UpdateWorkflowRequestDto>("{\"methodId\":null}", JsonOptions)!.MethodId.ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferencedDelete_ReturnsBothReferenceLists_AndNeverClearsAssociations(bool admin)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        var other = User();
        db.AiMethods.Add(method);
        db.Users.Add(other);
        await db.SaveChangesAsync();
        var task = Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, false, JsonSerializer.Serialize(method.Id)));
        var flow = Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, true, JsonSerializer.Serialize(method.Id)));
        var taskBefore = await Snapshot(db, false, DetailId(task));
        var flowBefore = await Snapshot(db, true, DetailId(flow));
        var caller = admin ? other : owner;
        var result = await new Methods(db, new FakeCurrentUserService(caller))
            .Delete(Principal(Query(("force", "true")), caller, admin ? "admin" : "contributor"), method.Id, Ct);
        var conflict = Assert.IsType<MethodDeleteConflictDto>(Assert.IsType<ConflictObjectResult>(result).Value);
        Assert.NotEmpty(conflict.Message);
        Assert.Equal(new MethodUsageDto(DetailId(task), "Parent"), Assert.Single(conflict.ReferencedByTasks));
        Assert.Equal(new MethodUsageDto(DetailId(flow), "Parent"), Assert.Single(conflict.ReferencedByWorkflows));
        Assert.Equal(taskBefore, await Snapshot(db, false, DetailId(task)));
        Assert.Equal(flowBefore, await Snapshot(db, true, DetailId(flow)));
        Assert.Single(db.AiMethods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelationalForeignKeys_BlockDirectDeletion_AndDanglingAssignment(bool workflow)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var created = Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow, JsonSerializer.Serialize(method.Id)));
        var id = DetailId(created);
        var before = await Snapshot(db, workflow, id);
        var delete = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiMethods WHERE Id = {method.Id}"));
        Assert.Equal(1811, delete.SqliteExtendedErrorCode);
        var missing = Guid.NewGuid();
        var assignment = await Assert.ThrowsAsync<SqliteException>(() => workflow
            ? db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Workflows SET MethodId = {missing} WHERE Id = {id}")
            : db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Tasks SET MethodId = {missing} WHERE Id = {id}"));
        Assert.Equal(787, assignment.SqliteExtendedErrorCode);
        Assert.Equal(before, await Snapshot(db, workflow, id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrentMethodRemoval_FailsAtRealForeignKey_WithoutPartialParentWrites(bool workflow, bool create)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        Guid id = Guid.Empty;
        string? before = null;
        if (!create)
        {
            id = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow)));
            before = await Snapshot(db, workflow, id);
        }
        hook.BeforeSave = async context =>
        {
            hook.BeforeSave = null;
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiMethods WHERE Id = {method.Id}");
        };
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => create
            ? Create(db, user, workflow, JsonSerializer.Serialize(method.Id))
            : Update(db, user, workflow, id, JsonSerializer.Serialize(method.Id), name: "Must roll back"));
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        if (create)
        {
            Assert.Empty(await db.Workflows.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Tasks.AsNoTracking().ToListAsync());
            Assert.Empty(await db.WorkflowSteps.AsNoTracking().ToListAsync());
        }
        else Assert.Equal(before, await Snapshot(db, workflow, id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SqlMethodForeignKeyFailure_ReturnsActionableValidation_WithoutPartialWrites(bool workflow, bool create)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        Guid id = Guid.Empty;
        string? before = null;
        if (!create)
        {
            id = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, user, workflow)));
            before = await Snapshot(db, workflow, id);
        }
        hook.BeforeSave = _ => throw SqlFailure(workflow ? "FK_Workflows_AiMethods_MethodId" : "FK_Tasks_AiMethods_MethodId");
        var result = create
            ? await Create(db, user, workflow, JsonSerializer.Serialize(method.Id))
            : await Update(db, user, workflow, id, JsonSerializer.Serialize(method.Id), name: "Must roll back");
        MethodsFunctionTests.AssertError(result);
        Assert.Contains("method", Assert.IsType<ErrorResponseDto>(Assert.IsType<BadRequestObjectResult>(result).Value).Message);
        if (create) Assert.Equal(0, workflow ? await db.Workflows.CountAsync() : await db.Tasks.CountAsync());
        else Assert.Equal(before, await Snapshot(db, workflow, id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqlDeleteRace_ReturnsNewReferences_WithoutClearingThem(bool workflow)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var id = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, workflow)));
        hook.BeforeSave = async context =>
        {
            if (workflow) await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Workflows SET MethodId = {method.Id} WHERE Id = {id}");
            else await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Tasks SET MethodId = {method.Id} WHERE Id = {id}");
            throw SqlFailure(workflow ? "FK_Workflows_AiMethods_MethodId" : "FK_Tasks_AiMethods_MethodId");
        };
        var result = await new Methods(db, new FakeCurrentUserService(owner)).Delete(Query(), method.Id, Ct);
        var body = Assert.IsType<MethodDeleteConflictDto>(Assert.IsType<ConflictObjectResult>(result).Value);
        Assert.Equal(id, Assert.Single(workflow ? body.ReferencedByWorkflows : body.ReferencedByTasks).Id);
        AssertAssociation(Assert.IsType<OkObjectResult>(await Get(db, owner, workflow, id)), method);
        Assert.Single(db.AiMethods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_DetectsRealRacingVersionOrRemoval(bool remove)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        hook.BeforeSave = async context =>
        {
            hook.BeforeSave = null;
            if (remove) await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiMethods WHERE Id = {method.Id}");
            else await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE AiMethods SET Version = {Guid.NewGuid()} WHERE Id = {method.Id}");
        };
        var result = await new Methods(db, new FakeCurrentUserService(owner)).Delete(Query(), method.Id, Ct);
        if (remove) Assert.IsType<NotFoundResult>(result);
        else
        {
            Assert.IsType<ErrorResponseDto>(Assert.IsType<ConflictObjectResult>(result).Value);
            Assert.Single(db.AiMethods);
        }
    }

    [Theory]
    [InlineData("FK_Artifacts_Users_CreatedByUserId", 547)]
    [InlineData("FK_WorkflowSteps_Artifacts_ArtifactId", 547)]
    [InlineData("FK_Tasks_AiMethods_MethodId", 2601)]
    public async Task UnrelatedSqlErrors_AreNotSwallowed(string constraint, int number)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var taskId = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, false)));
        var workflowId = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, true)));
        hook.BeforeSave = _ => throw SqlFailure(constraint, number);
        await Assert.ThrowsAsync<DbUpdateException>(() => Create(db, owner, false));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() => Update(db, owner, false, taskId));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() => Create(db, owner, true));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() => Update(db, owner, true, workflowId));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateException>(() => new Methods(db, new FakeCurrentUserService(owner)).Delete(Query(), method.Id, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workflow_SecondSaveFailure_RollsBackParentAndEveryOriginalStep(bool methodFailure)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var id = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, true)));
        await AddTaskBackedStep(db, owner, id);
        var before = await Snapshot(db, true, id);
        var saves = 0;
        hook.BeforeSave = async context =>
        {
            if (++saves != 2) return;
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Equal(0, await context.Set<WorkflowStep>().AsNoTracking().CountAsync(s => s.WorkflowId == id));
            if (methodFailure) throw SqlFailure("FK_Workflows_AiMethods_MethodId");
            // A real relational failure on the second save must also roll back the first save,
            // but must not be mislabeled as an invalid method.
            context.ChangeTracker.Entries<WorkflowStep>().First().Entity.TaskId = Guid.NewGuid();
        };
        if (methodFailure) MethodsFunctionTests.AssertError(await Update(db, owner, true, id, JsonSerializer.Serialize(method.Id), name: "Changed"));
        else
        {
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Update(db, owner, true, id, JsonSerializer.Serialize(method.Id), name: "Changed"));
            Assert.IsType<SqliteException>(ex.InnerException);
        }
        Assert.Equal(2, saves);
        Assert.Equal(before, await Snapshot(db, true, id));
    }

    [Fact]
    public async Task Workflow_RetryRestartsWholeTransaction_WithFreshStepsAndTrackedState()
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        var options = new DbContextOptionsBuilder<AppDbContext>(store.Options)
            .ReplaceService<IExecutionStrategyFactory, TestRetryFactory>().Options;
        await using var db = new AppDbContext(options);
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var id = DetailId(Assert.IsAssignableFrom<ObjectResult>(await Create(db, owner, true)));
        var saves = 0;
        hook.BeforeSave = context =>
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            if (++saves == 2) throw new RetryableException();
            return Task.CompletedTask;
        };
        var updated = Assert.IsType<OkObjectResult>(await Update(db, owner, true, id, JsonSerializer.Serialize(method.Id), name: "Retried"));
        AssertAssociation(updated, method);
        Assert.Equal(4, saves);
        Assert.Equal("Retried", Assert.IsType<WorkflowDetailDto>(updated.Value).Name);
        Assert.Equal(new[] { 1, 2 }, await db.WorkflowSteps.AsNoTracking().OrderBy(s => s.SequenceOrder).Select(s => s.SequenceOrder).ToArrayAsync());
    }

    [Fact]
    public async Task TaskUpload_RacingReferenceFailure_KeepsOldFile_AndCleansOnlyNewUpload()
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        var created = Assert.IsType<CreatedResult>(await Create(db, owner, false, blobs: blobs, file: true));
        var id = DetailId(created);
        var before = await Snapshot(db, false, id);
        var originalBlob = Assert.Single(blobs.UploadedBlobPaths);
        hook.BeforeSave = _ => throw SqlFailure("FK_Tasks_AiMethods_MethodId");
        MethodsFunctionTests.AssertError(await Update(db, owner, false, id, JsonSerializer.Serialize(method.Id),
            blobs: blobs, file: true, name: "Rejected"));
        Assert.Equal(before, await Snapshot(db, false, id));
        Assert.Equal(2, blobs.UploadedBlobPaths.Count);
        Assert.NotEqual(originalBlob, blobs.UploadedBlobPaths[1]);
        Assert.Equal(blobs.UploadedBlobPaths[1], Assert.Single(blobs.DeletedBlobPaths));
        hook.BeforeSave = null;
        var successful = Assert.IsType<OkObjectResult>(await Update(db, owner, false, id, JsonSerializer.Serialize(method.Id), blobs: blobs, file: true));
        AssertAssociation(successful, method);
        Assert.Equal("same.md", Assert.IsType<TaskDetailDto>(successful.Value).AttachedFile!.FileName);
        Assert.Equal(originalBlob, blobs.DeletedBlobPaths.Last());
        Assert.Equal(blobs.UploadedBlobPaths.Last(), (await db.Tasks.AsNoTracking().SingleAsync()).AttachedFileBlobPath);
    }

    [Fact]
    public async Task TaskCreateUpload_RacingReferenceFailure_LeavesNoParent_AndCleansUpload()
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        hook.BeforeSave = _ => throw SqlFailure("FK_Tasks_AiMethods_MethodId");
        MethodsFunctionTests.AssertError(await Create(db, owner, false, JsonSerializer.Serialize(method.Id), blobs, true));
        Assert.Empty(db.Tasks);
        Assert.Equal(Assert.Single(blobs.UploadedBlobPaths), Assert.Single(blobs.DeletedBlobPaths));
    }

    [Theory]
    [InlineData(true, 547)]
    [InlineData(false, 547)]
    [InlineData(true, 2601)]
    [InlineData(false, 2601)]
    [InlineData(true, 2627)]
    [InlineData(false, 2627)]
    [InlineData(true, 515)]
    [InlineData(false, 515)]
    [InlineData(true, 8152)]
    [InlineData(false, 8152)]
    [InlineData(true, 2628)]
    [InlineData(false, 2628)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    public async Task TaskUpload_ConfirmedUnrelatedRejection_CleansStagedFileAndPreservesOriginalError(bool create, int number)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        Guid id = Guid.Empty;
        string? before = null;
        if (!create)
        {
            id = DetailId(Assert.IsType<CreatedResult>(await Create(db, owner, false, blobs: blobs, file: true)));
            before = await Snapshot(db, false, id);
        }
        var error = number == 0 ? new DbUpdateConcurrencyException("Concurrent parent removal")
            : SqlFailure("UnrelatedConstraint", number);
        hook.BeforeSave = _ => throw error;
        var actual = await Assert.ThrowsAnyAsync<DbUpdateException>(() => create
            ? Create(db, owner, false, blobs: blobs, file: true)
            : Update(db, owner, false, id, blobs: blobs, file: true));
        Assert.Same(error, actual);
        Assert.Equal(blobs.UploadedBlobPaths.Last(), Assert.Single(blobs.DeletedBlobPaths));
        if (create) Assert.Empty(await db.Tasks.AsNoTracking().ToListAsync());
        else
        {
            Assert.NotEqual(blobs.UploadedBlobPaths.First(), blobs.DeletedBlobPaths.Single());
            Assert.Equal(before, await Snapshot(db, false, id));
        }
    }

    [Theory]
    [InlineData(true, -2)]
    [InlineData(false, -2)]
    [InlineData(true, 64)]
    [InlineData(false, 64)]
    [InlineData(true, 233)]
    [InlineData(false, 233)]
    public async Task TaskUpload_UncertainCommitOutcome_DoesNotDeleteEitherFile(bool create, int number)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        var id = create ? Guid.Empty : DetailId(Assert.IsType<CreatedResult>(
            await Create(db, owner, false, blobs: blobs, file: true)));
        var error = SqlFailure("Uncertain transport/timeout", number);
        hook.BeforeSave = _ => throw error;
        var actual = await Assert.ThrowsAsync<DbUpdateException>(() => create
            ? Create(db, owner, false, blobs: blobs, file: true)
            : Update(db, owner, false, id, blobs: blobs, file: true));
        Assert.Same(error, actual);
        Assert.Empty(blobs.DeletedBlobPaths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskUpload_RejectionAfterPriorCommittedAttempt_KeepsReferencedUpload(bool create)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService();
        var id = create ? Guid.Empty : DetailId(Assert.IsType<CreatedResult>(
            await Create(db, owner, false, blobs: blobs, file: true)));
        var error = SqlFailure("Duplicate after previous commit", 2627);
        hook.AfterSave = _ => throw error;
        var actual = await Assert.ThrowsAsync<DbUpdateException>(() => create
            ? Create(db, owner, false, blobs: blobs, file: true)
            : Update(db, owner, false, id, blobs: blobs, file: true));
        Assert.Same(error, actual);
        Assert.Equal(blobs.UploadedBlobPaths.Last(), (await db.Tasks.AsNoTracking().SingleAsync()).AttachedFileBlobPath);
        Assert.Empty(blobs.DeletedBlobPaths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskUpload_CleanupFailure_DoesNotMaskOriginalDatabaseRejection(bool create)
    {
        var hook = new SaveHook();
        await using var store = await RelationalTestDb.Create(hook);
        await using var db = store.Context();
        var owner = User();
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        var blobs = new FakeBlobStorageService { DeleteFailure = new Azure.RequestFailedException(503, "Storage unavailable") };
        var id = create ? Guid.Empty : DetailId(Assert.IsType<CreatedResult>(
            await Create(db, owner, false, blobs: blobs, file: true)));
        var error = SqlFailure("UnrelatedConstraint", 547);
        hook.BeforeSave = _ => throw error;
        var actual = await Assert.ThrowsAsync<DbUpdateException>(() => create
            ? Create(db, owner, false, blobs: blobs, file: true)
            : Update(db, owner, false, id, blobs: blobs, file: true));
        Assert.Same(error, actual);
        Assert.Equal(blobs.UploadedBlobPaths.Last(), Assert.Single(blobs.DeletedBlobPaths));
    }

    private static async Task AddTaskBackedStep(AppDbContext db, User owner, Guid workflowId)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(), Name = "Linked task", Description = "Source", InlineContent = "Prompt",
            CreatedByUserId = owner.Id, LastEditedByUserId = owner.Id, TaskType = "prompt",
        };
        db.Tasks.Add(task);
        var step = await db.WorkflowSteps.FirstAsync(s => s.WorkflowId == workflowId && s.SequenceOrder == 1);
        step.IsManualStep = false;
        step.TaskId = task.Id;
        step.TaskNameSnapshot = "Snapshot";
        step.TaskTypeSnapshot = "skill";
        step.ManualStepTitle = null;
        step.ManualStepInstructions = null;
        await db.SaveChangesAsync();
    }

    private static Task<IActionResult> Create(AppDbContext db, User user, bool workflow, string? methodJson = null,
        FakeBlobStorageService? blobs = null, bool file = false) => workflow
        ? new Workflows(db, Caller(db, user)).Create(Body(true, methodJson), Ct)
        : new Tasks(db, Caller(db, user), blobs ?? new(), new FakeFileValidationService()).Create(Body(false, methodJson, file: file), Ct);

    private static Task<IActionResult> Update(AppDbContext db, User user, bool workflow, Guid id, string? methodJson = null,
        DateTime? expected = null, string name = "Parent", FakeBlobStorageService? blobs = null, bool file = false) => workflow
        ? new Workflows(db, Caller(db, user)).Update(Body(true, methodJson, expected, name), id, Ct)
        : new Tasks(db, Caller(db, user), blobs ?? new(), new FakeFileValidationService()).Update(Body(false, methodJson, expected, name, file), id, Ct);

    private static Task<IActionResult> Get(AppDbContext db, User user, bool workflow, Guid id) => workflow
        ? new Workflows(db, Caller(db, user)).GetById(Query(), id, Ct)
        : new Tasks(db, Caller(db, user), new FakeBlobStorageService(), new FakeFileValidationService()).GetById(Query(), id, Ct);

    private static FakeCurrentUserService Caller(AppDbContext db, User user) => new(db.Users.Find(user.Id));

    private static HttpRequest Body(bool workflow, string? methodJson, DateTime? expected = null, string name = "Parent", bool file = false)
    {
        var fields = new Dictionary<string, object?>
        {
            ["name"] = name, ["description"] = "Description", ["taskType"] = "prompt",
            ["targetIds"] = new JsonArray("microsoft.copilot.m365", "github.copilot.vscode"), ["inlineContent"] = "Prompt content", ["referenceLink"] = "https://example.com",
            ["tags"] = new[] { "one", "two" }, ["dataSource"] = "Source", ["dataOutput"] = "Output",
            ["problemStatement"] = "Problem", ["desiredOutcome"] = "Outcome", ["expectedLastEditedAt"] = expected,
            ["steps"] = new[]
            {
                new { isManualStep = true, manualStepTitle = "First", manualStepInstructions = "Instructions 1",
                    notes = "Notes 1", dataSource = "Source 1", dataOutput = "Output 1", targetId = (string?)null },
                new { isManualStep = true, manualStepTitle = "Second", manualStepInstructions = "Instructions 2",
                    notes = "Notes 2", dataSource = "Source 2", dataOutput = "Output 2", targetId = (string?)null },
            },
        };
        if (methodJson is not null) fields["methodId"] = JsonSerializer.Deserialize<JsonElement>(methodJson);
        var json = JsonSerializer.Serialize(fields);
        if (workflow) return TestHttpRequestFactory.CreateWithJsonBody(json);
        return TestHttpRequestFactory.CreateWithForm(new() { ["metadata"] = new StringValues(json) },
            file ? new FormFileCollection { TestHttpRequestFactory.CreateFormFile("file", "same.md", "File content") } : null);
    }

    private static Guid DetailId(ObjectResult result) => result.Value switch
    {
        TaskDetailDto task => task.Id,
        WorkflowDetailDto workflow => workflow.Id,
        _ => throw new InvalidOperationException("Expected a detail response"),
    };

    private static void AssertAssociation(ObjectResult result, AiMethod? expected)
    {
        var (id, method) = result.Value switch
        {
            TaskDetailDto task => (task.MethodId, task.Method),
            WorkflowDetailDto workflow => (workflow.MethodId, workflow.Method),
            _ => throw new InvalidOperationException("Expected a detail response"),
        };
        Assert.Equal(expected?.Id, id);
        Assert.Equal(expected is null ? null : new MethodRefDto(expected.Id, expected.Title, expected.Summary), method);
        var json = JsonSerializer.SerializeToElement(result.Value, JsonOptions);
        Assert.True(json.TryGetProperty("methodId", out _));
        Assert.True(json.TryGetProperty("method", out _));
    }

    private static async Task<string> Snapshot(AppDbContext db, bool workflow, Guid id)
    {
        if (!workflow)
        {
            var task = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == id);
            return JsonSerializer.Serialize(new
            {
                task.Id, task.Name, task.Description, task.TaskType, task.TargetIds, task.Tags, task.ReferenceLink,
                task.InlineContent, task.DataSource, task.DataOutput, task.AttachedFileBlobPath, task.AttachedFileName,
                task.AttachedFileSizeBytes, task.CreatedByUserId, task.CreatedAt, task.LastEditedByUserId, task.LastEditedAt, task.MethodId,
            });
        }
        var flow = await db.Workflows.AsNoTracking().Include(w => w.Steps).SingleAsync(w => w.Id == id);
        return JsonSerializer.Serialize(new
        {
            flow.Id, flow.Name, flow.ProblemStatement, flow.DesiredOutcome, flow.CreatedByUserId, flow.CreatedAt,
            flow.LastEditedByUserId, flow.LastEditedAt, flow.MethodId,
            Steps = flow.Steps.OrderBy(s => s.SequenceOrder).Select(s => new
            {
                s.Id, s.WorkflowId, s.SequenceOrder, s.TaskId, s.TaskNameSnapshot, s.TaskTypeSnapshot, s.TargetId,
                s.Notes, s.IsManualStep, s.ManualStepTitle, s.ManualStepInstructions, s.DataSource, s.DataOutput,
            }),
        });
    }

    private sealed class RetryableException : Exception;
    public sealed class TestRetryFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new TestRetryStrategy(dependencies);
    }
    private sealed class TestRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is RetryableException;
    }
}
