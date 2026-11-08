using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class CatalogPagingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_PagesTiedTimestampsInIdOrder_AfterFiltering(bool workflows)
    {
        await using var db = TestDbContextFactory.Create();
        var user = new User { Id = Guid.NewGuid(), EntraObjectId = "paging-test", DisplayName = "Test" };
        db.Users.Add(user);
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expected = new List<Guid>();
        for (var i = 65; i >= 1; i--)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}");
            var name = i <= 60 ? $"Included {i}" : $"Excluded {i}";
            if (i <= 60) expected.Add(id);
            if (workflows)
                db.Workflows.Add(new Workflow
                {
                    Id = id, Name = name, ProblemStatement = "Problem", DesiredOutcome = "Outcome",
                    CreatedByUser = user, CreatedByUserId = user.Id,
                    LastEditedByUser = user, LastEditedByUserId = user.Id, LastEditedAt = timestamp,
                });
            else
                db.Tasks.Add(new TaskItem
                {
                    Id = id, Name = name, Description = "Description", TaskType = "prompt",
                    TargetIds = new() { "github.copilot.vscode" }, ReferenceLink = "https://example.com",
                    CreatedByUser = user, CreatedByUserId = user.Id,
                    LastEditedByUser = user, LastEditedByUserId = user.Id, LastEditedAt = timestamp,
                });
        }
        await db.SaveChangesAsync();
        expected.Sort();
        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
            {
                ["page"] = page.ToString(), ["pageSize"] = "25", ["q"] = "Included",
            });
            var action = workflows
                ? await new Workflows(db, new FakeCurrentUserService(user)).List(request, CancellationToken.None)
                : await new Tasks(db, new FakeCurrentUserService(user), new FakeBlobStorageService(),
                    new FakeFileValidationService()).List(request, CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(action);
            if (workflows)
            {
                var result = Assert.IsType<PagedResultDto<WorkflowSummaryDto>>(ok.Value);
                Assert.Equal(60, result.TotalCount);
                Assert.Equal(page == 3 ? 10 : 25, result.Items.Count);
                seen.AddRange(result.Items.Select(item => item.Id));
            }
            else
            {
                var result = Assert.IsType<PagedResultDto<TaskSummaryDto>>(ok.Value);
                Assert.Equal(60, result.TotalCount);
                Assert.Equal(page == 3 ? 10 : 25, result.Items.Count);
                seen.AddRange(result.Items.Select(item => item.Id));
            }
        }
        Assert.Equal(expected, seen);
        Assert.Equal(60, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_RejectsOverflowingPageOffset(bool workflows)
    {
        await using var db = TestDbContextFactory.Create();
        var request = TestHttpRequestFactory.CreateWithQuery(new Dictionary<string, StringValues>
        {
            ["page"] = int.MaxValue.ToString(), ["pageSize"] = "25",
        });
        var action = workflows
            ? await new Workflows(db, new FakeCurrentUserService(null)).List(request, CancellationToken.None)
            : await new Tasks(db, new FakeCurrentUserService(null), new FakeBlobStorageService(),
                new FakeFileValidationService()).List(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(action);
    }
}
