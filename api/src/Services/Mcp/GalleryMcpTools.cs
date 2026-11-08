using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// The outcome of one <c>tools/call</c>. A tool that could not answer reports
/// <see cref="IsError"/> with a human-readable message — it does not raise a JSON-RPC error,
/// because the protocol call itself succeeded.
/// </summary>
public sealed record McpToolResult(JsonNode? Payload, bool IsError, string? Message)
{
    public static McpToolResult Ok(JsonNode payload) => new(payload, false, null);

    public static McpToolResult Error(string message) => new(null, true, message);
}

public interface IGalleryMcpTools
{
    Task<McpToolResult> ExecuteAsync(
        string toolName,
        JsonObject? arguments,
        HttpRequest originating,
        CancellationToken cancellationToken);
}

/// <summary>
/// Executes the gallery's read capabilities by delegating to the very same Function classes the
/// browser calls (FR-001, FR-005).
///
/// <para>
/// <b>Delegation, not reimplementation.</b> Each tool assembles the query string the browser would
/// have sent and invokes <c>Tasks</c>, <c>Workflows</c>, <c>Methods</c> or <c>Reports</c> directly.
/// The filtering rules those endpoints implement are genuinely quirky — task search filters tags in
/// memory because the value converter blocks SQL translation, workflow search spans name, problem
/// statement and desired outcome, methods order by title while tasks order by last edited — and
/// re-expressing them here would create a second definition of "the same query" that drifts on the
/// next change. Delegating makes SC-002 structurally true instead of a thing to keep testing for.
/// </para>
/// <para>
/// <b>Never an empty success.</b> A missing item comes back from these endpoints as a 404, which is
/// mapped to a tool error carrying the id that was not found, so an agent cannot mistake "gone" for
/// "no results" (User Story 1, AS4).
/// </para>
/// </summary>
public class GalleryMcpTools(
    Tasks tasks,
    Workflows workflows,
    Methods methods,
    Reports reports,
    GalleryLinkBuilder linkBuilder) : IGalleryMcpTools
{
    /// <summary>
    /// Mirrors the outbound serialization configured in Program.cs (string enums) plus MVC's
    /// default camelCase naming, so an MCP payload is byte-for-byte the JSON the browser receives.
    /// Exposed to the test project so the parity tests compare against this exact configuration
    /// rather than a hand-maintained copy that could drift away from it.
    /// </summary>
    internal static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<McpToolResult> ExecuteAsync(
        string toolName,
        JsonObject? arguments,
        HttpRequest originating,
        CancellationToken cancellationToken)
    {
        var policy = PublicDemoPolicy.For(originating);
        if (policy.Enabled && !CollectionRequestContext.TryResolve(originating, policy, out var requestContext))
            throw new McpArgumentException("The MCP request must select exactly one valid collection.");
        var scopedArguments = arguments?.DeepClone().AsObject() ?? new JsonObject();
        var collectionId = new McpArguments(toolName, scopedArguments).RequiredCollectionId();
        if (policy.Enabled && collectionId != CollectionRequestContext.For(originating)!.CollectionId)
            throw new McpArgumentException("The tool collectionId must match the collection selected by the MCP connection.");
        if (policy.Enabled && !McpToolCatalog.IsPublicRead(toolName) &&
            (collectionId != CollectionScope.Playground || !policy.PlaygroundWritesEnabled))
            throw new McpArgumentException($"Unknown tool '{toolName}'.");
        scopedArguments.Remove("collectionId");
        var args = new McpArguments(toolName, scopedArguments);
        if (PublicDemoPolicy.For(originating).Enabled && scopedArguments.ContainsKey("pageSize") &&
            (scopedArguments["pageSize"] is not JsonValue pageSize ||
             !pageSize.TryGetValue<int>(out var size) || size is < 1 or > PublicMcpGuard.MaxPageSize))
            throw new McpArgumentException($"'pageSize' must be an integer from 1 to {PublicMcpGuard.MaxPageSize}.");
        originating = GalleryRequestFactory.ForCollection(originating, collectionId);

        return toolName switch
        {
            McpToolCatalog.SearchTasks => await SearchTasksAsync(args, originating, cancellationToken),
            McpToolCatalog.GetTask => await GetTaskAsync(args, originating, cancellationToken),
            McpToolCatalog.SearchWorkflows => await SearchWorkflowsAsync(args, originating, cancellationToken),
            McpToolCatalog.GetWorkflow => await GetWorkflowAsync(args, originating, cancellationToken),
            McpToolCatalog.SearchMethods => await SearchMethodsAsync(args, originating, cancellationToken),
            McpToolCatalog.GetMethod => await GetMethodAsync(args, originating, cancellationToken),
            McpToolCatalog.SearchReports => await SearchReportsAsync(args, originating, cancellationToken),
            McpToolCatalog.GetReport => await GetReportAsync(args, originating, cancellationToken),
            McpToolCatalog.CreateTask => await CreateTaskAsync(args, originating, cancellationToken),
            McpToolCatalog.UpdateTask => await UpdateTaskAsync(args, originating, cancellationToken),
            McpToolCatalog.DeleteTask => await DeleteTaskAsync(args, originating, cancellationToken),
            McpToolCatalog.CreateWorkflow => await CreateWorkflowAsync(args, originating, cancellationToken),
            McpToolCatalog.UpdateWorkflow => await UpdateWorkflowAsync(args, originating, cancellationToken),
            McpToolCatalog.DeleteWorkflow => await DeleteWorkflowAsync(args, originating, cancellationToken),
            McpToolCatalog.CreateMethod => await CreateMethodAsync(args, originating, cancellationToken),
            McpToolCatalog.UpdateMethod => await UpdateMethodAsync(args, originating, cancellationToken),
            McpToolCatalog.DeleteMethod => await DeleteMethodAsync(args, originating, cancellationToken),
            McpToolCatalog.CreateReport => await CreateReportAsync(args, originating, cancellationToken),
            McpToolCatalog.UpdateReport => await UpdateReportAsync(args, originating, cancellationToken),
            McpToolCatalog.DeleteReport => await DeleteReportAsync(args, originating, cancellationToken),
            _ => throw new McpArgumentException($"Unknown tool '{toolName}'."),
        };
    }

    private async Task<McpToolResult> SearchTasksAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("q", "taskType", "targetId", "page", "pageSize");

        // `type` is the query parameter name the tasks endpoint uses; the tool argument is named
        // `taskType` so it reads the same way as it does on search_workflows.
        var request = GalleryRequestFactory.Create(originating, new Dictionary<string, string?>
        {
            ["q"] = args.OptionalSearch("q"),
            ["type"] = args.RegistryId("taskType", CatalogRegistry.Default.KindIds),
            ["targetId"] = args.RegistryId("targetId", CatalogRegistry.Default.TargetIds),
            ["page"] = args.Page().ToString(),
            ["pageSize"] = args.PageSize().ToString(),
        });

        return Project(await tasks.List(request, ct), originating, GalleryLinkBuilder.TaskPath);
    }

    private async Task<McpToolResult> GetTaskAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id");
        var id = args.RequiredId("id");
        var request = GalleryRequestFactory.Create(originating);

        return Project(await tasks.GetById(request, id, ct), originating, GalleryLinkBuilder.TaskPath, "task", id);
    }

    private async Task<McpToolResult> SearchWorkflowsAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("q", "taskId", "taskType", "targetId", "page", "pageSize");

        var request = GalleryRequestFactory.Create(originating, new Dictionary<string, string?>
        {
            ["q"] = args.OptionalSearch("q"),
            ["taskId"] = args.OptionalId("taskId"),
            ["taskType"] = args.RegistryId("taskType", CatalogRegistry.Default.KindIds),
            ["targetId"] = args.RegistryId("targetId", CatalogRegistry.Default.TargetIds),
            ["page"] = args.Page().ToString(),
            ["pageSize"] = args.PageSize().ToString(),
        });

        return Project(await workflows.List(request, ct), originating, GalleryLinkBuilder.WorkflowPath);
    }

    private async Task<McpToolResult> GetWorkflowAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id");
        var id = args.RequiredId("id");
        var request = GalleryRequestFactory.Create(originating);

        return Project(await workflows.GetById(request, id, ct), originating, GalleryLinkBuilder.WorkflowPath, "workflow", id);
    }

    private async Task<McpToolResult> SearchMethodsAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("q", "page", "pageSize");

        var request = GalleryRequestFactory.Create(originating, new Dictionary<string, string?>
        {
            ["q"] = args.OptionalSearch("q"),
            ["page"] = args.Page().ToString(),
            ["pageSize"] = args.PageSize().ToString(),
        });

        return Project(await methods.List(request, ct), originating, GalleryLinkBuilder.MethodPath);
    }

    private async Task<McpToolResult> GetMethodAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id");
        var id = args.RequiredId("id");
        var request = GalleryRequestFactory.Create(originating);

        return Project(await methods.GetById(request, id, ct), originating, GalleryLinkBuilder.MethodPath, "AI method", id);
    }

    private async Task<McpToolResult> SearchReportsAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("q", "kind", "page", "pageSize");

        var request = GalleryRequestFactory.Create(originating, new Dictionary<string, string?>
        {
            ["q"] = args.OptionalSearch("q"),
            ["kind"] = args.OptionalEnum<ReportLinkKind>("kind"),
            ["page"] = args.Page().ToString(),
            ["pageSize"] = args.PageSize().ToString(),
        });

        return Project(await reports.List(request, ct), originating, GalleryLinkBuilder.ReportPath);
    }

    private async Task<McpToolResult> GetReportAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id");
        var id = args.RequiredId("id");
        var request = GalleryRequestFactory.Create(originating);

        return Project(await reports.GetById(request, id, ct), originating, GalleryLinkBuilder.ReportPath, "report or AI app", id);
    }

    /// <summary>Task arguments shared by create and update, mirroring McpToolCatalog.TaskFields.</summary>
    private static readonly string[] SharedTaskArguments =
        ["taskType", "targetIds", "format", "setup", "prerequisites", "tags", "referenceLink", "inlineContent", "dataSource", "dataOutput", "methodId"];

    private async Task<McpToolResult> CreateTaskAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown([.. SharedTaskArguments, "name", "description"]);

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var metadata = new JsonObject
        {
            ["name"] = args.RequiredText("name", McpToolCatalog.MaxNameLength),
            ["description"] = args.RequiredText("description", McpToolCatalog.MaxDescriptionLength),
            ["taskType"] = args.RegistryId("taskType", CatalogRegistry.Default.KindIds),
        };

        ApplyOptionalTaskFields(args, metadata, existing: null);

        var request = GalleryRequestFactory.CreateForm(originating, HttpMethods.Post, new Dictionary<string, string?>
        {
            ["metadata"] = metadata.ToJsonString(),
        });

        return Project(await tasks.Create(request, ct), originating, GalleryLinkBuilder.TaskPath);
    }

    /// <summary>
    /// Updates a task by reading it first, overlaying only the arguments the caller supplied, and
    /// submitting the whole thing.
    ///
    /// <para>
    /// <b>Why read-modify-write.</b> <c>PUT /api/tasks/{id}</c> is a full replace: every field is
    /// assigned from the metadata part, so anything the caller leaves out is cleared. The browser
    /// never trips over this because its form is populated from the current record before the
    /// person edits it — submitting "everything" is a side effect of how a form works. An agent has
    /// no such form, so sending only the fields it means to change would quietly wipe the tags,
    /// link and content someone curated. Reading first reproduces what the browser actually does,
    /// and makes "update the description" mean only that.
    /// </para>
    /// <para>
    /// This does not weaken the concurrency guard: <c>expectedLastEditedAt</c> stays the caller's
    /// argument, taken from whenever <em>it</em> last looked, and is checked against the database at
    /// write time. Overlaying onto a fresher read cannot mask a conflict, because the guard never
    /// consults this read.
    /// </para>
    /// </summary>
    private async Task<McpToolResult> UpdateTaskAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown([.. SharedTaskArguments, "id", "name", "description", "expectedLastEditedAt"]);

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        var current = await tasks.GetById(GalleryRequestFactory.Create(originating), id, ct);
        if (current is not ObjectResult { Value: Dtos.TaskDetailDto existing })
        {
            // Not found, unauthorized or an error — report it on the read's own terms rather than
            // attempting a write that is certain to fail for the same reason.
            return Project(current, originating, GalleryLinkBuilder.TaskPath, "task", id);
        }

        var metadata = new JsonObject
        {
            ["name"] = args.Has("name")
                ? args.RequiredText("name", McpToolCatalog.MaxNameLength)
                : existing.Name,
            ["description"] = args.Has("description")
                ? args.RequiredText("description", McpToolCatalog.MaxDescriptionLength)
                : existing.Description,
            ["taskType"] = args.RegistryId("taskType", CatalogRegistry.Default.KindIds) ?? existing.TaskType,
        };

        ApplyOptionalTaskFields(args, metadata, existing);

        if (args.OptionalTimestamp("expectedLastEditedAt") is { } expected)
        {
            metadata["expectedLastEditedAt"] = expected.ToString("o");
        }

        var request = GalleryRequestFactory.CreateForm(originating, HttpMethods.Put, new Dictionary<string, string?>
        {
            ["metadata"] = metadata.ToJsonString(),
        });

        return Project(await tasks.Update(request, id, ct), originating, GalleryLinkBuilder.TaskPath, "task", id);
    }

    /// <summary>
    /// Writes the optional task fields onto <paramref name="metadata"/>. An argument the caller did
    /// not supply falls back to <paramref name="existing"/> (or is omitted entirely on create), and
    /// one supplied as blank clears the field — which is why every value goes through
    /// <see cref="McpArguments.Has"/> rather than a null check.
    /// </summary>
    private static void ApplyOptionalTaskFields(McpArguments args, JsonObject metadata, Dtos.TaskDetailDto? existing)
    {
        var targets = args.Has("targetIds") ? args.TargetIds() : existing?.TargetIds;
        metadata["targetIds"] = targets is null ? null : new JsonArray(targets.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        metadata["format"] = args.Has("format")
            ? args.Text("format") is null ? null : args.RegistryId("format", CatalogRegistry.Default.FormatIds)
            : existing?.Format;
        metadata["setup"] = args.Has("setup") ? args.Text("setup", 10000) : existing?.Setup;
        metadata["prerequisites"] = args.Has("prerequisites") ? args.Text("prerequisites", 10000) : existing?.Prerequisites;
        metadata["referenceLink"] = args.Has("referenceLink")
            ? args.Text("referenceLink", McpToolCatalog.MaxReferenceLinkLength)
            : existing?.ReferenceLink;
        metadata["inlineContent"] = args.Has("inlineContent") ? args.Text("inlineContent") : existing?.InlineContent;
        metadata["dataSource"] = args.Has("dataSource")
            ? args.Text("dataSource", McpToolCatalog.MaxDataSourceLength)
            : existing?.DataSource;
        metadata["dataOutput"] = args.Has("dataOutput")
            ? args.Text("dataOutput", McpToolCatalog.MaxDataOutputLength)
            : existing?.DataOutput;

        var tags = args.Has("tags") ? args.TagArray("tags") : existing?.Tags;
        if (tags is not null)
        {
            metadata["tags"] = new JsonArray([.. tags.Select(t => (JsonNode)t)]);
        }

        // Absent means "leave the association alone"; an explicitly empty string removes it, the
        // same signal every other optional field here uses. Re-sending the existing id on update
        // keeps the association through an otherwise unrelated edit.
        if (args.Has("methodId"))
        {
            metadata["methodId"] = args.Text("methodId") is null ? null : args.OptionalId("methodId");
        }
        else if (existing?.MethodId is { } methodId)
        {
            metadata["methodId"] = methodId.ToString();
        }
    }

    private async Task<McpToolResult> DeleteTaskAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "confirm");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");
        var confirm = args.OptionalBool("confirm");

        var request = GalleryRequestFactory.CreateBodyless(originating, HttpMethods.Delete,
            confirm ? new Dictionary<string, string?> { ["confirm"] = "true" } : null);

        return Project(await tasks.Delete(request, id, ct), originating, GalleryLinkBuilder.TaskPath, "task", id);
    }

    private static readonly string[] SharedWorkflowArguments = ["steps", "methodId"];

    private async Task<McpToolResult> CreateWorkflowAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown([.. SharedWorkflowArguments, "name", "problemStatement", "desiredOutcome"]);

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var body = new JsonObject
        {
            ["name"] = args.RequiredText("name", McpToolCatalog.MaxNameLength),
            ["problemStatement"] = args.RequiredText("problemStatement", McpToolCatalog.MaxDescriptionLength),
            ["desiredOutcome"] = args.RequiredText("desiredOutcome", McpToolCatalog.MaxDescriptionLength),
            ["steps"] = BuildSteps(args)
                ?? throw new McpArgumentException(
                    "Tool 'create_workflow' requires a 'steps' array — a workflow is a sequence, " +
                    "and needs at least two steps before it can be saved."),
        };

        ApplyWorkflowMethod(args, body, existingMethodId: null);

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Post, body);

        return Project(await workflows.Create(request, ct), originating, GalleryLinkBuilder.WorkflowPath);
    }

    /// <summary>
    /// Updates a workflow by reading it first and overlaying only what the caller supplied, for the
    /// same reason <see cref="UpdateTaskAsync"/> does — <c>PUT /api/workflows/{id}</c> is a full
    /// replace.
    ///
    /// <para>
    /// Steps make this sharper than it is for a task. The endpoint requires at least two steps on
    /// every write, so an agent changing only the name would not merely lose the sequence, it could
    /// not save at all. Omitting <c>steps</c> therefore re-submits the existing sequence, and
    /// supplying it replaces the sequence outright — there is no partial step edit, because the
    /// endpoint deletes and rebuilds the rows wholesale and a step has no stable identifier a
    /// caller could address.
    /// </para>
    /// </summary>
    private async Task<McpToolResult> UpdateWorkflowAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown(
            [.. SharedWorkflowArguments, "id", "name", "problemStatement", "desiredOutcome", "expectedLastEditedAt"]);

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        var current = await workflows.GetById(GalleryRequestFactory.Create(originating), id, ct);
        if (current is not ObjectResult { Value: Dtos.WorkflowDetailDto existing })
        {
            return Project(current, originating, GalleryLinkBuilder.WorkflowPath, "workflow", id);
        }

        JsonArray steps;
        if (args.Has("steps"))
        {
            steps = BuildSteps(args)!;
        }
        else if (RebuildExistingSteps(existing) is { } rebuilt)
        {
            steps = rebuilt;
        }
        else
        {
            // A step whose task was deleted keeps a name snapshot but no task id, and the write
            // contract has no way to express that — the browser cannot save such a workflow either.
            // Re-submitting it would either fail validation or silently convert the step into
            // something it is not, so say plainly what has to happen first.
            return McpToolResult.Error(
                $"Workflow '{existing.Name}' has a step whose task has been deleted, so it cannot " +
                "be saved until that step is replaced or removed. Supply the complete 'steps' " +
                "array to fix the sequence, or ask someone to repair the workflow in the gallery.");
        }

        var body = new JsonObject
        {
            ["name"] = args.Has("name")
                ? args.RequiredText("name", McpToolCatalog.MaxNameLength)
                : existing.Name,
            ["problemStatement"] = args.Has("problemStatement")
                ? args.RequiredText("problemStatement", McpToolCatalog.MaxDescriptionLength)
                : existing.ProblemStatement,
            ["desiredOutcome"] = args.Has("desiredOutcome")
                ? args.RequiredText("desiredOutcome", McpToolCatalog.MaxDescriptionLength)
                : existing.DesiredOutcome,
            ["steps"] = steps,
        };

        ApplyWorkflowMethod(args, body, existing.MethodId);

        if (args.OptionalTimestamp("expectedLastEditedAt") is { } expected)
        {
            body["expectedLastEditedAt"] = expected.ToString("o");
        }
        else if (!args.Has("steps"))
        {
            // The caller is not editing the sequence, so everything in `steps` above came from the
            // read a moment ago. Submitting it unguarded would delete whatever someone else
            // committed in between and rebuild the sequence from this stale read, silently. The
            // browser never does that: its form is populated from a read and submits that read's
            // timestamp, so a concurrent edit surfaces as a conflict. Do the same here.
            body["expectedLastEditedAt"] = existing.LastEditedAt.ToString("o");
        }

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Put, body);

        return Project(
            await workflows.Update(request, id, ct), originating, GalleryLinkBuilder.WorkflowPath, "workflow", id);
    }

    /// <summary>
    /// Binds the supplied <c>steps</c> array into the shape the workflows endpoint deserializes.
    /// Returns null when the caller supplied no steps at all.
    /// </summary>
    private static JsonArray? BuildSteps(McpArguments args)
    {
        var steps = args.ObjectArray("steps");
        if (steps is null)
        {
            return null;
        }

        var result = new JsonArray();
        foreach (var step in steps)
        {
            step.RejectUnknown(
                "taskId", "isManualStep", "manualStepTitle", "manualStepInstructions",
                "targetId", "notes", "dataSource", "dataOutput");

            var isManual = step.OptionalBool("isManualStep");

            var json = new JsonObject
            {
                ["isManualStep"] = isManual,
                ["targetId"] = step.RegistryId("targetId", CatalogRegistry.Default.TargetIds),
                ["notes"] = step.Text("notes", McpToolCatalog.MaxNotesLength),
                ["dataSource"] = step.Text("dataSource", McpToolCatalog.MaxDataSourceLength),
                ["dataOutput"] = step.Text("dataOutput", McpToolCatalog.MaxDataOutputLength),
            };

            if (isManual)
            {
                if (step.Has("taskId") || step.Has("targetId"))
                    throw new McpArgumentException("Manual steps must have null taskId and targetId.");
                json["manualStepTitle"] = step.RequiredText("manualStepTitle", McpToolCatalog.MaxNameLength);
                json["manualStepInstructions"] =
                    step.Text("manualStepInstructions", McpToolCatalog.MaxStepInstructionsLength);
            }
            else
            {
                // The endpoint rejects this too, but only as "every step must reference a task",
                // which does not say which step — unhelpful when a sequence is being assembled.
                json["taskId"] = step.OptionalId("taskId")
                    ?? throw new McpArgumentException(
                        $"Each step needs either a 'taskId' or 'isManualStep': true. Step " +
                        $"{result.Count + 1} has neither. Use search_tasks to find the task it runs.");
            }

            result.Add(json);
        }

        return result;
    }

    /// <summary>
    /// Re-expresses a workflow's current steps as write input, so an edit that does not mention
    /// steps leaves the sequence exactly as it was. Returns null when a step cannot be expressed —
    /// which happens only when its task has been deleted.
    /// </summary>
    private static JsonArray? RebuildExistingSteps(Dtos.WorkflowDetailDto existing)
    {
        var result = new JsonArray();

        foreach (var step in existing.Steps)
        {
            if (!step.IsManualStep && step.TaskId is null)
            {
                return null;
            }

            var json = new JsonObject
            {
                ["isManualStep"] = step.IsManualStep,
                ["targetId"] = step.TargetId,
                ["notes"] = step.Notes,
                ["dataSource"] = step.DataSource,
                ["dataOutput"] = step.DataOutput,
            };

            if (step.IsManualStep)
            {
                // A manual step has no task, so the detail projection puts its title in taskName.
                json["manualStepTitle"] = step.TaskName;
                json["manualStepInstructions"] = step.ManualStepInstructions;
            }
            else
            {
                json["taskId"] = step.TaskId!.Value.ToString();
            }

            result.Add(json);
        }

        return result;
    }

    /// <summary>
    /// Applies the method association using the same absent/blank distinction the task tools use.
    /// </summary>
    private static void ApplyWorkflowMethod(McpArguments args, JsonObject body, Guid? existingMethodId)
    {
        if (args.Has("methodId"))
        {
            body["methodId"] = args.Text("methodId") is null ? null : args.OptionalId("methodId");
        }
        else if (existingMethodId is { } methodId)
        {
            body["methodId"] = methodId.ToString();
        }
    }

    private async Task<McpToolResult> DeleteWorkflowAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "confirm");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        if (!args.OptionalBool("confirm"))
        {
            // Unlike a task, nothing downstream guards this: the endpoint deletes the workflow and
            // cascades its steps without asking. Describing what would go, and requiring a second
            // deliberate call, is the only chance a person gets to stop it.
            var current = await workflows.GetById(GalleryRequestFactory.Create(originating), id, ct);
            if (current is not ObjectResult { Value: Dtos.WorkflowDetailDto existing })
            {
                return Project(current, originating, GalleryLinkBuilder.WorkflowPath, "workflow", id);
            }

            return McpToolResult.Error(
                $"Deleting '{existing.Name}' would permanently remove it and its " +
                $"{existing.Steps.Count} step(s), and cannot be undone. Show this to the person and " +
                "re-run with confirm: true if they agree. The tasks the steps refer to are kept.");
        }

        var request = GalleryRequestFactory.CreateBodyless(originating, HttpMethods.Delete);

        return Project(
            await workflows.Delete(request, id, ct), originating, GalleryLinkBuilder.WorkflowPath, "workflow", id);
    }

    /// <summary>
    /// Creates an AI method.
    ///
    /// <para>
    /// Methods and directory entries guard concurrency with an opaque <c>version</c> rather than
    /// the <c>lastEditedAt</c> tasks and workflows use, and their endpoints require it on every
    /// update — there is no last-write-wins option to fall back on. That makes the write path
    /// safer by default, and it is why these tools carry <c>expectedVersion</c> instead.
    /// </para>
    /// </summary>
    private async Task<McpToolResult> CreateMethodAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("title", "summary", "approach");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var body = new JsonObject
        {
            ["title"] = args.RequiredText("title", McpToolCatalog.MaxNameLength),
            ["summary"] = args.RequiredText("summary", McpToolCatalog.MaxDescriptionLength),
            ["approach"] = args.RequiredText("approach", McpToolCatalog.MaxApproachLength),
        };

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Post, body);

        return Project(await methods.Create(request, ct), originating, GalleryLinkBuilder.MethodPath);
    }

    /// <summary>
    /// Updates an AI method by reading it first and overlaying only what the caller supplied, for
    /// the same reason <see cref="UpdateTaskAsync"/> does — the endpoint replaces every field.
    ///
    /// <para>
    /// The guard is never omitted. The endpoint refuses an update that carries no
    /// <c>expectedVersion</c> at all, so when the caller does not supply one the version from this
    /// call's own read is used. That is not a formality: the rest of the body is built from that
    /// same read, so without it a narrow edit would write back a stale title or approach over
    /// someone else's work and report success.
    /// </para>
    /// </summary>
    private async Task<McpToolResult> UpdateMethodAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "title", "summary", "approach", "expectedVersion");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        var current = await methods.GetById(GalleryRequestFactory.Create(originating), id, ct);
        if (current is not ObjectResult { Value: Dtos.MethodDetailDto existing })
        {
            return Project(current, originating, GalleryLinkBuilder.MethodPath, "AI method", id);
        }

        var body = new JsonObject
        {
            ["title"] = args.Has("title")
                ? args.RequiredText("title", McpToolCatalog.MaxNameLength)
                : existing.Title,
            ["summary"] = args.Has("summary")
                ? args.RequiredText("summary", McpToolCatalog.MaxDescriptionLength)
                : existing.Summary,
            ["approach"] = args.Has("approach")
                ? args.RequiredText("approach", McpToolCatalog.MaxApproachLength)
                : existing.Approach,
            ["expectedVersion"] = args.OptionalId("expectedVersion") ?? existing.Version.ToString(),
        };

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Put, body);

        return Project(await methods.Update(request, id, ct), originating, GalleryLinkBuilder.MethodPath, "AI method", id);
    }

    private async Task<McpToolResult> DeleteMethodAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "confirm");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        if (!args.OptionalBool("confirm"))
        {
            // The endpoint refuses to delete a method anything still references, but nothing stops
            // it removing one that happens to be unreferenced today — and a method is the most
            // re-used content the gallery holds. Describe it and require a second deliberate call.
            var current = await methods.GetById(GalleryRequestFactory.Create(originating), id, ct);
            if (current is not ObjectResult { Value: Dtos.MethodDetailDto existing })
            {
                return Project(current, originating, GalleryLinkBuilder.MethodPath, "AI method", id);
            }

            return McpToolResult.Error(
                $"Deleting the AI method '{existing.Title}' would permanently remove its written " +
                "approach, and cannot be undone. Show this to the person and re-run with " +
                "confirm: true if they agree. If any task or workflow is still based on it, the " +
                "deletion will be refused and those items listed.");
        }

        var request = GalleryRequestFactory.CreateBodyless(originating, HttpMethods.Delete);

        return Project(await methods.Delete(request, id, ct), originating, GalleryLinkBuilder.MethodPath, "AI method", id);
    }

    private async Task<McpToolResult> CreateReportAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("name", "description", "url", "kind");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var body = new JsonObject
        {
            ["name"] = args.RequiredText("name", McpToolCatalog.MaxNameLength),
            ["description"] = args.RequiredText("description", McpToolCatalog.MaxReportDescriptionLength),
            ["url"] = args.RequiredText("url", McpToolCatalog.MaxUrlLength),
            ["kind"] = args.RequiredEnum<ReportLinkKind>("kind"),
        };

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Post, body);

        return Project(await reports.Create(request, ct), originating, GalleryLinkBuilder.ReportPath);
    }

    /// <summary>
    /// Updates a directory entry, read-modify-write like the others, with the same never-omitted
    /// version guard as <see cref="UpdateMethodAsync"/>.
    /// </summary>
    private async Task<McpToolResult> UpdateReportAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "name", "description", "url", "kind", "expectedVersion");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        var current = await reports.GetById(GalleryRequestFactory.Create(originating), id, ct);
        if (current is not ObjectResult { Value: Dtos.ReportLinkDto existing })
        {
            return Project(current, originating, GalleryLinkBuilder.ReportPath, "report or AI app", id);
        }

        var body = new JsonObject
        {
            ["name"] = args.Has("name")
                ? args.RequiredText("name", McpToolCatalog.MaxNameLength)
                : existing.Name,
            ["description"] = args.Has("description")
                ? args.RequiredText("description", McpToolCatalog.MaxReportDescriptionLength)
                : existing.Description,
            ["url"] = args.Has("url")
                ? args.RequiredText("url", McpToolCatalog.MaxUrlLength)
                : existing.Url,
            ["kind"] = args.Has("kind")
                ? args.RequiredEnum<ReportLinkKind>("kind")
                : existing.Kind.ToString(),
            ["expectedVersion"] = args.OptionalId("expectedVersion") ?? existing.Version.ToString(),
        };

        var request = GalleryRequestFactory.CreateJson(originating, HttpMethods.Put, body);

        return Project(await reports.Update(request, id, ct), originating, GalleryLinkBuilder.ReportPath, "report or AI app", id);
    }

    private async Task<McpToolResult> DeleteReportAsync(McpArguments args, HttpRequest originating, CancellationToken ct)
    {
        args.RejectUnknown("id", "confirm");

        if (RequireNamedPrincipal(originating) is { } refusal)
        {
            return refusal;
        }

        var id = args.RequiredId("id");

        if (!args.OptionalBool("confirm"))
        {
            // As with a workflow, nothing downstream guards this at all.
            var current = await reports.GetById(GalleryRequestFactory.Create(originating), id, ct);
            if (current is not ObjectResult { Value: Dtos.ReportLinkDto existing })
            {
                return Project(current, originating, GalleryLinkBuilder.ReportPath, "report or AI app", id);
            }

            return McpToolResult.Error(
                $"Deleting '{existing.Name}' would permanently remove its directory entry, and " +
                "cannot be undone. Show this to the person and re-run with confirm: true if they " +
                $"agree. The destination itself ({existing.Url}) is not affected.");
        }

        var request = GalleryRequestFactory.CreateBodyless(originating, HttpMethods.Delete);

        return Project(await reports.Delete(request, id, ct), originating, GalleryLinkBuilder.ReportPath, "report or AI app", id);
    }

    /// <summary>
    /// Private writes require the authenticated caller. Public Playground writes use the
    /// provisioned sandbox actor and never manufacture directory identity claims.
    /// </summary>
    private static McpToolResult? RequireNamedPrincipal(HttpRequest originating)
    {
        var context = CollectionRequestContext.For(originating);
        if (context?.Capabilities is { IsPublicDemo: true, CanWrite: true } &&
            context.CollectionId == CollectionScope.Playground)
            return null;

        return Auth.GalleryIdentity.Key(originating) is not null
            ? null
            : McpToolResult.Error(
                "This write cannot be attributed to a signed-in person, so it was refused. " +
                "Re-authenticate to the gallery and retry.");
    }

    /// <summary>
    /// Turns the delegated endpoint's <see cref="IActionResult"/> into a tool result, adding the
    /// back-link and mapping the failure statuses those endpoints already use onto tool errors.
    /// </summary>
    private McpToolResult Project(
        IActionResult result,
        HttpRequest originating,
        string path,
        string? itemKind = null,
        Guid? requestedId = null)
    {
        switch (result)
        {
            case NotFoundResult or NotFoundObjectResult:
                return McpToolResult.Error(
                    itemKind is null || requestedId is null
                        ? "The requested item does not exist."
                        : $"No {itemKind} exists with id {requestedId}.");

            case UnauthorizedResult or UnauthorizedObjectResult:
                return McpToolResult.Error("Sign-in is required, or your session has expired. Re-authenticate and retry.");

            // Only the AI methods endpoint answers this, and it does so with a bare status and no
            // body — so without this arm a permission refusal would fall through to the "unexpected
            // response" default and the agent would never learn it simply was not allowed.
            case StatusCodeResult { StatusCode: StatusCodes.Status403Forbidden }
                or ObjectResult { StatusCode: StatusCodes.Status403Forbidden }:
                return McpToolResult.Error(
                    itemKind is null
                        ? "Refused: the signed-in person is not allowed to do this."
                        : $"Refused: only the person who created this {itemKind}, or an " +
                          "administrator, may do this. Nothing was changed.");

            // A delete answers 204 with no body. Reporting a small explicit confirmation beats an
            // empty success, for the same reason a missing item is an error rather than silence.
            case NoContentResult:
                return McpToolResult.Ok(new JsonObject
                {
                    ["deleted"] = true,
                    ["id"] = requestedId?.ToString(),
                });

            // A conflict carries the item's *current* state, which is exactly what the caller needs
            // to merge its change and retry — so it is spelled out in the message rather than
            // reduced to "409". The result is still an error: nothing was written.
            case ObjectResult { StatusCode: 409 } conflict:
                return McpToolResult.Error(DescribeConflict(conflict.Value));

            case ObjectResult { StatusCode: >= 400 } failed:
                return McpToolResult.Error(DescribeFailure(failed.Value));

            case ObjectResult { Value: not null } ok:
                var payload = JsonSerializer.SerializeToNode(ok.Value, ok.Value.GetType(), PayloadOptions)!;
                AddLinks(payload, originating, path);
                return McpToolResult.Ok(payload);

            default:
                // Every read path above returns one of the cases handled here; anything else means
                // an endpoint changed shape, and failing loudly beats returning a blank success.
                return McpToolResult.Error("The gallery returned an unexpected response.");
        }
    }

    /// <summary>
    /// Adds a <c>galleryUrl</c> back-link to a detail payload, or to every item of a paged payload.
    /// Items without an <c>id</c> are left alone rather than guessed at.
    ///
    /// <para>
    /// The field is deliberately <em>not</em> called <c>url</c>: a report-link payload already has
    /// a <c>url</c> of its own — the destination the gallery merely points at — and overwriting it
    /// would both corrupt the record and blur the line FR-010 draws between a link the gallery
    /// stores and a page the gallery owns.
    /// </para>
    /// </summary>
    private void AddLinks(JsonNode payload, HttpRequest originating, string path)
    {
        if (payload is not JsonObject obj)
        {
            return;
        }

        if (obj["items"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                AddLink(item, originating, path);
            }

            return;
        }

        AddLink(obj, originating, path);
    }

    private void AddLink(JsonObject item, HttpRequest originating, string path)
    {
        if (item["id"]?.GetValue<string>() is { } rawId && Guid.TryParse(rawId, out var id) &&
            linkBuilder.BuildItemUrl(originating, path, id) is { } url)
        {
            item["galleryUrl"] = url;
        }
    }

    /// <summary>
    /// Surfaces the delegated endpoint's own error message when it has one, so an agent is told
    /// exactly what the browser would have been told.
    /// </summary>
    private static string DescribeFailure(object? value) => value switch
    {
        Dtos.ErrorResponseDto error => error.Message,
        null => "The gallery rejected the request.",
        _ => TryReadErrorText(value) ?? JsonSerializer.Serialize(value, value.GetType(), PayloadOptions),
    };

    /// <summary>
    /// Explains a refused write and hands back whatever the endpoint returned alongside it. The
    /// delete guard returns the workflows that would be affected; a stale edit returns the item as
    /// it now stands, including the <c>lastEditedAt</c> the caller must echo back to retry.
    /// </summary>
    private static string DescribeConflict(object? value) => value switch
    {
        // Only `Tasks.Delete` honours a `confirm` query parameter, so only its conflict may be
        // overridden. `Methods.Delete` refuses a referenced method unconditionally, and the tool's
        // own confirm gate never forwards the flag — telling an agent to retry with it would send
        // it round a loop that cannot make progress.
        Dtos.DeleteConflictDto =>
            "Refused: other gallery content still references this item, and nothing was deleted. " +
            "Show the references below to the person, and only re-run with confirm: true if they agree. " +
            Serialize(value),

        Dtos.MethodDeleteConflictDto =>
            "Refused: the tasks and workflows below still reference this method, and nothing was " +
            "deleted. There is no override — they must be unlinked from the method first. Do not " +
            "retry with confirm: true. " + Serialize(value),

        Dtos.ErrorResponseDto error => $"Refused: {error.Message} Nothing was written.",

        null => "Refused as a conflict. Nothing was written.",

        // The reports endpoint reports both its validation errors and its conflicts as a bare
        // `{ "error": "..." }`, so it never reaches the fallback below. Reading that text out is
        // what keeps its message as precise for an agent as it is for the browser.
        _ when TryReadErrorText(value) is { } message => $"Refused: {message} Nothing was written.",

        // Only the task and workflow endpoints reach this fallback — the two that guard with an
        // edit timestamp — so the retry instruction can name the exact argument to send back.
        _ => "Refused: this item changed since you last read it, and nothing was written. " +
             "Re-apply your change on top of the current version below, passing its lastEditedAt " +
             "as expectedLastEditedAt. " + Serialize(value),
    };

    /// <summary>
    /// Reads the message out of an endpoint's anonymous <c>{ "error": "..." }</c> response. The
    /// gallery is not consistent here — some endpoints return a typed
    /// <see cref="Dtos.ErrorResponseDto"/>, others an anonymous object — and changing either shape
    /// would change what the browser receives, so both are read rather than unified (FR-011).
    /// </summary>
    private static string? TryReadErrorText(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), PayloadOptions);

        return node is JsonObject obj
            && obj.Count == 1
            && obj["error"] is { } error
            && error.GetValueKind() == JsonValueKind.String
            ? error.GetValue<string>()
            : null;
    }

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), PayloadOptions);
}
