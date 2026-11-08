using System.Text.Json.Nodes;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Mcp;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// The capability catalog must be self-describing (FR-006) and must never advertise what policy
/// has switched off (FR-007, User Story 3 AS1).
/// </summary>
public class McpToolCatalogTests
{
    private static McpToolCatalog Catalog(bool writesEnabled = false) =>
        new(new McpOptions { Enabled = true, WritesEnabled = writesEnabled });

    [Fact]
    public void Catalog_CoversEveryContentType_ForSearchAndRetrieval()
    {
        var names = Catalog().Visible.Select(t => t.Name).ToList();

        Assert.Equal(
            [
                "search_tasks", "get_task",
                "search_workflows", "get_workflow",
                "search_methods", "get_method",
                "search_reports", "get_report",
            ],
            names);
    }

    [Fact]
    public void EverySchema_IsAClosedObject_WithDescribedArguments()
    {
        // Writes on, so the write schemas are held to the same self-description bar (FR-006).
        foreach (var tool in Catalog(writesEnabled: true).Visible)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), $"{tool.Name} has no title.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description.");

            var schema = tool.InputSchema;
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());

            var properties = Assert.IsType<JsonObject>(schema["properties"]);
            Assert.NotEmpty(properties);

            foreach (var (name, property) in properties)
            {
                var propertySchema = Assert.IsType<JsonObject>(property);
                Assert.NotNull(propertySchema["type"]);
                Assert.False(
                    string.IsNullOrWhiteSpace(propertySchema["description"]?.GetValue<string>()),
                    $"{tool.Name}.{name} has no description.");
            }
        }
    }

    [Fact]
    public void AllToolsRequireCollection_AndRetrievalAlsoRequiresId()
    {
        foreach (var tool in Catalog().Visible)
        {
            var required = tool.InputSchema["required"] as JsonArray;

            if (tool.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                Assert.NotNull(required);
                Assert.Equal(new[] { "id", "collectionId" }, required!.Select(x => x!.GetValue<string>()));
            }
            else
            {
                Assert.Equal("collectionId", Assert.Single(required!)!.GetValue<string>());
            }
        }
    }

    [Fact]
    public void EnumArguments_AdvertiseExactlyTheDomainValues()
    {
        var searchTasks = Catalog().Find("search_tasks")!;
        var properties = (JsonObject)searchTasks.InputSchema["properties"]!;

        Assert.Equal(WorkflowGallery.Api.Services.CatalogRegistry.Default.KindIds,
            ((JsonArray)properties["taskType"]!["enum"]!).Select(x => x!.GetValue<string>()));
        Assert.Equal(WorkflowGallery.Api.Services.CatalogRegistry.Default.TargetIds,
            ((JsonArray)properties["targetId"]!["enum"]!).Select(x => x!.GetValue<string>()));
        AssertEnum<ReportLinkKind>(((JsonObject)Catalog().Find("search_reports")!.InputSchema["properties"]!)["kind"]!);

        static void AssertEnum<TEnum>(JsonNode property) where TEnum : struct, Enum =>
            Assert.Equal(
                Enum.GetNames<TEnum>(),
                ((JsonArray)property["enum"]!).Select(v => v!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void PageSizeArgument_AdvertisesTheExistingMaximum_NotANewOne()
    {
        var properties = (JsonObject)Catalog().Find("search_workflows")!.InputSchema["properties"]!;

        Assert.Equal(100, properties["pageSize"]!["maximum"]!.GetValue<int>());
        Assert.Equal(1, properties["pageSize"]!["minimum"]!.GetValue<int>());
        Assert.Equal(1, properties["page"]!["minimum"]!.GetValue<int>());
    }

    [Fact]
    public void EveryReadTool_AnnouncesItselfAsReadOnlyAndClosedWorld()
    {
        foreach (var tool in Catalog().Visible)
        {
            var annotations = (JsonObject)tool.ToJson()["annotations"]!;

            Assert.True(annotations["readOnlyHint"]!.GetValue<bool>());
            Assert.False(annotations["destructiveHint"]!.GetValue<bool>());

            // FR-010 in machine-readable form: nothing here reaches outside the gallery.
            Assert.False(annotations["openWorldHint"]!.GetValue<bool>());
        }
    }

    [Fact]
    public void WriteTools_AnnounceTheirRealConsequences()
    {
        var byName = Catalog(writesEnabled: true).Visible.ToDictionary(t => t.Name);

        static JsonObject Annotations(McpToolDefinition tool) => (JsonObject)tool.ToJson()["annotations"]!;

        foreach (var name in WriteToolNames)
        {
            Assert.False(Annotations(byName[name])["readOnlyHint"]!.GetValue<bool>());
            Assert.False(Annotations(byName[name])["openWorldHint"]!.GetValue<bool>());
        }

        // A client decides how hard to confirm with the person from these two flags, so a blanket
        // "harmless and safe to retry" would be an outright lie. The update tools count as
        // destructive because they can clear a field or replace a whole step sequence — neither is
        // an additive-only change.
        Assert.True(Annotations(byName["delete_task"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_task"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["delete_workflow"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_workflow"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["delete_method"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_method"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["delete_report"])["destructiveHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_report"])["destructiveHint"]!.GetValue<bool>());

        Assert.False(Annotations(byName["create_task"])["idempotentHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_task"])["idempotentHint"]!.GetValue<bool>());
        Assert.False(Annotations(byName["create_workflow"])["idempotentHint"]!.GetValue<bool>());
        Assert.True(Annotations(byName["update_workflow"])["idempotentHint"]!.GetValue<bool>());
        Assert.False(Annotations(byName["create_method"])["idempotentHint"]!.GetValue<bool>());
        Assert.False(Annotations(byName["create_report"])["idempotentHint"]!.GetValue<bool>());
    }

    /// <summary>Every tool that writes, so a new one cannot be added without these guarantees.</summary>
    private static readonly string[] WriteToolNames =
    [
        "create_task", "update_task", "delete_task",
        "create_workflow", "update_workflow", "delete_workflow",
        "create_method", "update_method", "delete_method",
        "create_report", "update_report", "delete_report",
    ];

    [Fact]
    public void WriteCapabilities_AreNeitherAdvertisedNorResolvable_WhenWritesAreOff()
    {
        var off = Catalog(writesEnabled: false);
        var on = Catalog(writesEnabled: true);

        foreach (var name in WriteToolNames)
        {
            // Absent from tools/list *and* unresolvable on tools/call, so turning writes off does
            // not leak the shape of what is behind the switch (FR-007, User Story 3 AS1).
            Assert.DoesNotContain(off.Visible, t => t.Name == name);
            Assert.Null(off.Find(name));
            Assert.NotNull(on.Find(name));
        }

        Assert.All(off.Visible, t => Assert.False(t.RequiresWrite));
    }

    [Fact]
    public void TaskWriteTools_ShareOneFieldVocabulary()
    {
        var on = Catalog(writesEnabled: true);
        var create = (JsonObject)on.Find("create_task")!.InputSchema["properties"]!;
        var update = (JsonObject)on.Find("update_task")!.InputSchema["properties"]!;

        // Update is create plus the identifier and the concurrency guard — nothing else. If the two
        // drift apart, an agent that can create a task can no longer edit the same fields back.
        Assert.Equal(
            create.Select(p => p.Key).Concat(["id", "expectedLastEditedAt"]).OrderBy(n => n, StringComparer.Ordinal),
            update.Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal));

        Assert.Equal(
            ["name", "description", "taskType", "targetIds", "collectionId"],
            ((JsonArray)on.Find("create_task")!.InputSchema["required"]!).Select(v => v!.GetValue<string>()));

        Assert.Equal(new[] { "id", "collectionId" },
            ((JsonArray)on.Find("update_task")!.InputSchema["required"]!).Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void WorkflowWriteTools_ShareOneFieldVocabulary()
    {
        var on = Catalog(writesEnabled: true);
        var create = (JsonObject)on.Find("create_workflow")!.InputSchema["properties"]!;
        var update = (JsonObject)on.Find("update_workflow")!.InputSchema["properties"]!;

        Assert.Equal(
            create.Select(p => p.Key).Concat(["id", "expectedLastEditedAt"]).OrderBy(n => n, StringComparer.Ordinal),
            update.Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal));

        // Steps are required on create, because a workflow with fewer than two is not saveable —
        // and the minimum is advertised so a client can catch it before calling.
        Assert.Equal(
            ["name", "problemStatement", "desiredOutcome", "steps", "collectionId"],
            ((JsonArray)on.Find("create_workflow")!.InputSchema["required"]!).Select(v => v!.GetValue<string>()));

        Assert.Equal(2, create["steps"]!["minItems"]!.GetValue<int>());

        // Omitting steps on update means "leave the sequence alone", so it cannot be required.
        Assert.Equal(new[] { "id", "collectionId" },
            ((JsonArray)on.Find("update_workflow")!.InputSchema["required"]!).Select(x => x!.GetValue<string>()));
    }

    /// <summary>
    /// AI methods and directory entries version with an opaque token rather than an edit timestamp,
    /// so their guard argument is `expectedVersion`. It belongs only to update: there is nothing to
    /// expect before the item exists, and the create tools reject it.
    /// </summary>
    [Theory]
    [InlineData("method")]
    [InlineData("report")]
    public void VersionedWriteTools_ShareOneFieldVocabulary_AndGuardOnlyOnUpdate(string kind)
    {
        var on = Catalog(writesEnabled: true);
        var create = (JsonObject)on.Find($"create_{kind}")!.InputSchema["properties"]!;
        var update = (JsonObject)on.Find($"update_{kind}")!.InputSchema["properties"]!;

        Assert.Equal(
            create.Select(p => p.Key).Concat(["id", "expectedVersion"]).OrderBy(n => n, StringComparer.Ordinal),
            update.Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal));

        Assert.DoesNotContain("expectedVersion", create.Select(p => p.Key));
        Assert.Equal("uuid", update["expectedVersion"]!["format"]!.GetValue<string>());

        Assert.Equal(new[] { "id", "collectionId" },
            ((JsonArray)on.Find($"update_{kind}")!.InputSchema["required"]!).Select(x => x!.GetValue<string>()));
    }
}
