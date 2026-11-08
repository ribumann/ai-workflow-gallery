using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>Smoke tests for `/api/tasks`.</summary>
public class TasksFunctionTests
{
    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = $"entra-{Guid.NewGuid()}",
        DisplayName = "Test User",
    };

    [Fact]
    public async Task List_ReturnsSeededTasks()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        db.Users.Add(user);
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Sample Agent",
            Description = "A sample agent for search.",
            TaskType = "agent",
            TargetIds = new() { "github.copilot.vscode" },
            ReferenceLink = "https://example.com",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>());

        var result = await function.List(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResultDto<TaskSummaryDto>>(ok.Value);
        Assert.Single(page.Items);
        Assert.Equal("Sample Agent", page.Items[0].Name);
    }

    [Fact]
    public async Task List_FiltersByTagSearch_CaseInsensitive()
    {
        // Regression test for issue #67: `q` search previously ran `t.ToLower().Contains(...)`
        // directly against `TaskItem.Tags`, a List<string> mapped via an EF Core value converter
        // to a single comma-joined string column. EF Core cannot translate a per-element lambda
        // over a value-converted property to SQL against a real relational provider (SQL Server),
        // so every search request threw `InvalidOperationException` in production even though the
        // EF Core InMemory provider used by these tests never surfaced it. The fix now filters by
        // type/toolSurface in SQL, then matches name/description/tags in memory.
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        db.Users.Add(user);
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Sample Agent",
            Description = "A sample agent for search.",
            TaskType = "agent",
            TargetIds = new() { "github.copilot.vscode" },
            Tags = new List<string> { "productivity", "onboarding" },
            ReferenceLink = "https://example.com",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        });
        db.Tasks.Add(new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Unrelated Prompt",
            Description = "Nothing matching here.",
            TaskType = "prompt",
            TargetIds = new() { "microsoft.copilot.m365" },
            Tags = new List<string> { "misc" },
            ReferenceLink = "https://example.com",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
        {
            ["q"] = "ONBOARDING",
        });

        var result = await function.List(request, CancellationToken.None);

        var ok2 = Assert.IsType<OkObjectResult>(result);
        var page2 = Assert.IsType<PagedResultDto<TaskSummaryDto>>(ok2.Value);
        Assert.Single(page2.Items);
        Assert.Equal("Sample Agent", page2.Items[0].Name);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMissing()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Tasks(db, new FakeCurrentUserService(null), new FakeBlobStorageService(), new FakeFileValidationService());

        var result = await function.GetById(TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>()), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsDetailFieldsAndUserDisplayNames()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Visible task",
            Description = "A task detail.",
            TaskType = "prompt",
            TargetIds = new() { "microsoft.copilot.m365" },
            InlineContent = "Prompt content",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        db.Users.Add(user);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var function = new Tasks(db, new FakeCurrentUserService(null), new FakeBlobStorageService(), new FakeFileValidationService());
        var result = await function.GetById(
            TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>()),
            task.Id,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var detail = Assert.IsType<TaskDetailDto>(ok.Value);
        Assert.Equal(task.Id, detail.Id);
        Assert.Equal(task.Name, detail.Name);
        Assert.Equal("Test User", detail.CreatedBy.DisplayName);
        Assert.Equal("Test User", detail.LastEditedBy.DisplayName);
        Assert.Equal("Prompt content", detail.InlineContent);
    }

    [Fact]
    public async Task Create_ReturnsUnauthorized_WhenNoCurrentUser()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Tasks(db, new FakeCurrentUserService(null), new FakeBlobStorageService(), new FakeFileValidationService());

        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = """{"name":"A","description":"B","taskType":"agent","targetIds":["github.copilot.vscode"],"referenceLink":"https://example.com"}""",
        });

        var result = await function.Create(request, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithValidMetadataAndReferenceLink()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());

        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = """{"name":"New Agent","description":"Does useful things.","taskType":"agent","targetIds":["github.copilot.vscode"],"tags":["demo"],"referenceLink":"https://example.com"}""",
        });

        var result = await function.Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        var dto = Assert.IsType<TaskDetailDto>(created.Value);
        Assert.Equal("New Agent", dto.Name);
        Assert.Single(await db.Tasks.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenNoContentSourceProvided()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());

        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = """{"name":"New Agent","description":"Does useful things.","taskType":"agent","targetIds":["github.copilot.vscode"]}""",
        });

        var result = await function.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("not a url")]
    public async Task Create_ReturnsBadRequest_WhenReferenceLinkIsNotHttp(string unsafeLink)
    {
        // Regression test for issue #67: adversarial review found `javascript:` URIs were accepted
        // and stored verbatim, then rendered as a clickable link on the task detail page (XSS).
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());

        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = $$"""{"name":"New Agent","description":"Does useful things.","taskType":"agent","targetIds":["github.copilot.vscode"],"referenceLink":"{{unsafeLink}}"}""",
        });

        var result = await function.Create(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Tasks.ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithDataSourceAndDataOutput()
    {
        // Regression test for issue #67: tasks can optionally record the data source they
        // consume and the data output they produce, surfaced on the detail DTO.
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());

        var request = TestHttpRequestFactory.CreateWithForm(new Dictionary<string, StringValues>
        {
            ["metadata"] = """{"name":"Data Agent","description":"Transforms data.","taskType":"agent","targetIds":["github.copilot.vscode"],"referenceLink":"https://example.com","dataSource":"Raw CSV export","dataOutput":"Normalized JSON"}""",
        });

        var result = await function.Create(request, CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        var dto = Assert.IsType<TaskDetailDto>(created.Value);
        Assert.Equal("Raw CSV export", dto.DataSource);
        Assert.Equal("Normalized JSON", dto.DataOutput);
    }

    [Fact]
    public async Task Delete_ReturnsConflict_WhenReferencedByWorkflow_AndNotConfirmed()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Referenced Task",
            Description = "Used by a workflow.",
            TaskType = "skill",
            TargetIds = new() { "github.copilot.vscode" },
            ReferenceLink = "https://example.com",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "A Workflow",
            ProblemStatement = "Problem",
            DesiredOutcome = "Outcome",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        db.Users.Add(user);
        db.Tasks.Add(task);
        db.Workflows.Add(workflow);
        db.WorkflowSteps.Add(new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            SequenceOrder = 1,
            TaskId = task.Id,
            TargetId = "github.copilot.vscode",
        });
        await db.SaveChangesAsync();

        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>());

        var result = await function.Delete(request, task.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Delete_Succeeds_WhenConfirmedDespiteReference()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Name = "Referenced Task",
            Description = "Used by a workflow.",
            TaskType = "skill",
            TargetIds = new() { "github.copilot.vscode" },
            ReferenceLink = "https://example.com",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "A Workflow",
            ProblemStatement = "Problem",
            DesiredOutcome = "Outcome",
            CreatedByUser = user,
            CreatedByUserId = user.Id,
            LastEditedByUser = user,
            LastEditedByUserId = user.Id,
        };
        db.Users.Add(user);
        db.Tasks.Add(task);
        db.Workflows.Add(workflow);
        db.WorkflowSteps.Add(new WorkflowStep
        {
            Id = Guid.NewGuid(),
            WorkflowId = workflow.Id,
            SequenceOrder = 1,
            TaskId = task.Id,
            TargetId = "github.copilot.vscode",
        });
        await db.SaveChangesAsync();

        var function = new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(), new FakeFileValidationService());
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues> { ["confirm"] = "true" });

        var result = await function.Delete(request, task.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var survivingStep = Assert.Single(db.WorkflowSteps.Local);
        Assert.Null(survivingStep.TaskId);
        Assert.Equal("Referenced Task", survivingStep.TaskNameSnapshot);
    }
}
