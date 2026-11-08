using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// Unit tests for <see cref="AzureAiComposerService.ResolveAgainstCatalog"/> (T091, FR-004,
/// issue #59) — the pure mapping/validation step that cross-checks every model-referenced
/// task id against the live catalog, independent of the actual AI Foundry model call.
/// </summary>
public class AiComposerCatalogValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveAgainstCatalog_RejectsMissingRequiredFields(string name)
    {
        var raw = new AzureAiComposerService.RawProposal(name, "Problem", "Outcome", [], []);
        Assert.Throws<AiComposerUnavailableException>(() => AzureAiComposerService.ResolveAgainstCatalog(raw, []));
    }

    [Fact]
    public void ResolveAgainstCatalog_RejectsOversizedCollectionsAndFields()
    {
        var steps = Enumerable.Range(1, 51).Select(i => new AzureAiComposerService.RawStep(i, Guid.NewGuid().ToString(), null, "microsoft.copilot.m365")).ToList();
        var raw = new AzureAiComposerService.RawProposal("Name", "Problem", "Outcome", steps, []);
        Assert.Throws<AiComposerUnavailableException>(() => AzureAiComposerService.ResolveAgainstCatalog(raw, []));
        raw = new AzureAiComposerService.RawProposal(new string('n', 201), "Problem", "Outcome", [], []);
        Assert.Throws<AiComposerUnavailableException>(() => AzureAiComposerService.ResolveAgainstCatalog(raw, []));
    }

    private static CatalogTaskSnapshot NewCatalogTask(Guid id, string name = "Task") =>
        new(id, name, "Description", "prompt", new[] { "microsoft.copilot.m365" }, new List<string>());

    [Fact]
    public void ResolveAgainstCatalog_ResolvesStep_WhenTaskIdExistsInCatalog()
    {
        var taskId = Guid.NewGuid();
        var catalog = new List<CatalogTaskSnapshot> { NewCatalogTask(taskId, "Real Task") };
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep> { new(1, taskId.ToString(), "Some notes", "microsoft.copilot.m365") },
            new List<AzureAiComposerService.RawGap>());

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        var step = Assert.Single(result.Steps);
        Assert.Equal(taskId, step.TaskId);
        Assert.Equal("Real Task", step.TaskName);
        Assert.Equal("Some notes", step.Notes);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void ResolveAgainstCatalog_DemotesStepToGap_WhenTaskIdDoesNotExistInCatalog()
    {
        var catalog = new List<CatalogTaskSnapshot> { NewCatalogTask(Guid.NewGuid()) };
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep> { new(1, Guid.NewGuid().ToString(), "Notes", "microsoft.copilot.m365") },
            new List<AzureAiComposerService.RawGap>());

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        Assert.Empty(result.Steps);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal(1, gap.SequenceHint);
    }

    [Fact]
    public void ResolveAgainstCatalog_DemotesStepToGap_WhenTaskIdIsNotAValidGuid()
    {
        var catalog = new List<CatalogTaskSnapshot> { NewCatalogTask(Guid.NewGuid()) };
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep> { new(1, "not-a-guid", "Notes", "microsoft.copilot.m365") },
            new List<AzureAiComposerService.RawGap>());

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        Assert.Empty(result.Steps);
        Assert.Single(result.Gaps);
    }

    [Fact]
    public void ResolveAgainstCatalog_OrdersResolvedStepsBySequenceOrder()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var catalog = new List<CatalogTaskSnapshot>
        {
            NewCatalogTask(firstId, "First"),
            NewCatalogTask(secondId, "Second"),
        };
        // Deliberately out of order in the raw response.
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep>
            {
                new(2, secondId.ToString(), null, "microsoft.copilot.m365"),
                new(1, firstId.ToString(), null, "microsoft.copilot.m365"),
            },
            new List<AzureAiComposerService.RawGap>());

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(1, result.Steps[0].SequenceOrder);
        Assert.Equal(2, result.Steps[1].SequenceOrder);
    }

    [Fact]
    public void ResolveAgainstCatalog_PassesThroughModelSuggestedGaps()
    {
        var catalog = new List<CatalogTaskSnapshot> { NewCatalogTask(Guid.NewGuid()) };
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep>(),
            new List<AzureAiComposerService.RawGap> { new("No task covers this part", 3, "New Skill", "Does X") });

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        Assert.Empty(result.Steps);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal("No task covers this part", gap.Description);
        Assert.Equal("New Skill", gap.SuggestedTaskName);
        Assert.Equal("Does X", gap.SuggestedTaskDescription);
    }

    [Fact]
    public void ResolveAgainstCatalog_AddsFallbackGap_WhenNoStepsAndNoGapsResolve()
    {
        var catalog = new List<CatalogTaskSnapshot> { NewCatalogTask(Guid.NewGuid()) };
        var raw = new AzureAiComposerService.RawProposal(
            "Name", "Problem", "Outcome",
            new List<AzureAiComposerService.RawStep>(),
            new List<AzureAiComposerService.RawGap>());

        var result = AzureAiComposerService.ResolveAgainstCatalog(raw, catalog);

        Assert.Empty(result.Steps);
        Assert.Single(result.Gaps);
    }
}
