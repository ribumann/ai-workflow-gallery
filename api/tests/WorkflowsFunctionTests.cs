using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Smoke tests for `/api/workflows`.</summary>
public class WorkflowsFunctionTests
{
    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = $"entra-{Guid.NewGuid()}",
        DisplayName = "Test User",
    };

    private static TaskItem NewTask(User owner) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Task",
        Description = "Description",
        TaskType = "prompt",
        TargetIds = new() { "microsoft.copilot.m365", "github.copilot.vscode" },
        ReferenceLink = "https://example.com",
        CreatedByUser = owner,
        CreatedByUserId = owner.Id,
        LastEditedByUser = owner,
        LastEditedByUserId = owner.Id,
    };

    [Fact]
    public async Task List_ReturnsSeededWorkflows()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        db.Users.Add(user);
        db.Workflows.Add(new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Sample Workflow",
            ProblemStatement = "Problem",
            DesiredOutcome = "Outcome",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var result = await function.List(TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>()), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResultDto<WorkflowSummaryDto>>(ok.Value);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Workflows(db, new FakeCurrentUserService(null));

        var result = await function.GetById(TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>()), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsScopedWorkflowAndOrderedSteps()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Visible workflow",
            ProblemStatement = "Problem",
            DesiredOutcome = "Outcome",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        workflow.Steps.Add(new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            SequenceOrder = 1,
            TaskId = task.Id,
            Task = task,
            TargetId = "microsoft.copilot.m365",
        });
        db.Users.Add(user);
        db.Tasks.Add(task);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(null));
        var result = await function.GetById(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>()),
            workflow.Id,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<WorkflowDetailDto>(ok.Value);
        Assert.Equal(workflow.Id, detail.Id);
        Assert.Equal("Test User", detail.CreatedBy.DisplayName);
        var step = Assert.Single(detail.Steps);
        Assert.Equal(task.Id, step.TaskId);
        Assert.Equal(task.Name, step.TaskName);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenFewerThanTwoSteps()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var json = $$"""
        {
          "name": "Too Short",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [ { "taskId": "{{task.Id}}", "targetId": "microsoft.copilot.m365" } ]
        }
        """;

        var result = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithTwoValidSteps()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task1 = NewTask(user);
        var task2 = NewTask(user);
        db.Users.Add(user);
        db.Tasks.AddRange(task1, task2);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var json = $$"""
        {
          "name": "Two Step Workflow",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            { "taskId": "{{task1.Id}}", "targetId": "microsoft.copilot.m365" },
            { "taskId": "{{task2.Id}}", "targetId": "github.copilot.vscode", "notes": "second step" }
          ]
        }
        """;

        var result = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<WorkflowDetailDto>(created.Value);
        Assert.Equal(2, dto.Steps.Count);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenTaskDoesNotExist()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var missingId = Guid.NewGuid();
        var json = $$"""
        {
          "name": "Bad Workflow",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            { "taskId": "{{task.Id}}", "targetId": "microsoft.copilot.m365" },
            { "taskId": "{{missingId}}", "targetId": "github.copilot.vscode" }
          ]
        }
        """;

        var result = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Update_ReplacesSteps_AndReturnsOk()
    {
        // Regression test for issue #67: updating a workflow's steps previously reassigned the
        // `Steps` navigation property directly after RemoveRange'ing the old ones in the same
        // SaveChanges call, which threw `DbUpdateConcurrencyException` against SQL Server in
        // production (never surfaced by this suite's EF Core InMemory provider). The fix flushes
        // the step deletion in its own SaveChanges call before adding the replacement steps.
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task1 = NewTask(user);
        var task2 = NewTask(user);
        var task3 = NewTask(user);
        db.Users.Add(user);
        db.Tasks.AddRange(task1, task2, task3);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var createJson = $$"""
        {
          "name": "Original Workflow",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            { "taskId": "{{task1.Id}}", "targetId": "microsoft.copilot.m365" },
            { "taskId": "{{task2.Id}}", "targetId": "github.copilot.vscode" }
          ]
        }
        """;
        var createResult = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(createJson), CancellationToken.None);
        var created = Assert.IsType<ObjectResult>(createResult);
        var createdDto = Assert.IsType<WorkflowDetailDto>(created.Value);

        var updateJson = $$"""
        {
          "name": "Updated Workflow",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            { "taskId": "{{task2.Id}}", "targetId": "github.copilot.vscode" },
            { "taskId": "{{task3.Id}}", "targetId": "microsoft.copilot.m365" }
          ]
        }
        """;
        var updateResult = await function.Update(TestHttpRequestFactory.CreateWithJsonBody(updateJson), createdDto.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(updateResult);
        var updatedDto = Assert.IsType<WorkflowDetailDto>(ok.Value);
        Assert.Equal("Updated Workflow", updatedDto.Name);
        Assert.Equal(2, updatedDto.Steps.Count);
        Assert.Equal(task2.Id, updatedDto.Steps[0].TaskId);
        Assert.Equal(task3.Id, updatedDto.Steps[1].TaskId);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithManualStepAndDataFields()
    {
        // Regression test for issue #67: workflows can mix task-backed steps with manual
        // (no-task) steps, and both step kinds accept optional dataSource/dataOutput fields.
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var json = $$"""
        {
          "name": "Mixed Workflow",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            {
              "taskId": "{{task.Id}}",
              "targetId": "microsoft.copilot.m365",
              "dataSource": "Customer CRM export",
              "dataOutput": "Enriched lead list"
            },
            {
              "isManualStep": true,
              "manualStepTitle": "Review with stakeholder",
              "manualStepInstructions": "Walk through the output with the account team.",
              "targetId": null
            }
          ]
        }
        """;

        var result = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var dto = Assert.IsType<WorkflowDetailDto>(created.Value);
        Assert.Equal(2, dto.Steps.Count);

        var taskStep = dto.Steps[0];
        Assert.False(taskStep.IsManualStep);
        Assert.Equal(task.Id, taskStep.TaskId);
        Assert.Equal("Customer CRM export", taskStep.DataSource);
        Assert.Equal("Enriched lead list", taskStep.DataOutput);

        var manualStep = dto.Steps[1];
        Assert.True(manualStep.IsManualStep);
        Assert.Null(manualStep.TaskId);
        Assert.Equal("Review with stakeholder", manualStep.TaskName);
        Assert.Equal("Walk through the output with the account team.", manualStep.ManualStepInstructions);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenManualStepHasNoTitle()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = NewTask(user);
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Workflows(db, new FakeCurrentUserService(user));
        var json = $$"""
        {
          "name": "Invalid Manual Step",
          "problemStatement": "Problem",
          "desiredOutcome": "Outcome",
          "steps": [
            { "taskId": "{{task.Id}}", "targetId": "microsoft.copilot.m365" },
            { "isManualStep": true, "targetId": null }
          ]
        }
        """;

        var result = await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
