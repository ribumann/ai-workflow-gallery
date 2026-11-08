using System.Text.Json.Nodes;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// The catalog of capabilities the MCP server offers (FR-001, FR-006).
///
/// <para>
/// <b>One list, two consumers.</b> <c>tools/list</c> and <c>tools/call</c> both resolve through
/// <see cref="Visible"/>/<see cref="Find"/>, which apply the same policy filter. That is what makes
/// FR-007's "disabled capabilities MUST NOT be advertised" hold in both directions: a gated tool is
/// absent from the catalog *and* indistinguishable from a tool that never existed when called, so
/// turning writes off does not leak the shape of what is behind the switch.
/// </para>
/// </summary>
public class McpToolCatalog
{
    public const string SearchTasks = "search_tasks";
    public const string GetTask = "get_task";
    public const string SearchWorkflows = "search_workflows";
    public const string GetWorkflow = "get_workflow";
    public const string SearchMethods = "search_methods";
    public const string GetMethod = "get_method";
    public const string SearchReports = "search_reports";
    public const string GetReport = "get_report";
    public const string CreateTask = "create_task";
    public const string UpdateTask = "update_task";
    public const string DeleteTask = "delete_task";
    public const string CreateWorkflow = "create_workflow";
    public const string UpdateWorkflow = "update_workflow";
    public const string DeleteWorkflow = "delete_workflow";
    public const string CreateMethod = "create_method";
    public const string UpdateMethod = "update_method";
    public const string DeleteMethod = "delete_method";
    public const string CreateReport = "create_report";
    public const string UpdateReport = "update_report";
    public const string DeleteReport = "delete_report";

    public static bool IsPublicRead(string name) => name is
        SearchTasks or GetTask or SearchWorkflows or GetWorkflow or
        SearchMethods or GetMethod or SearchReports or GetReport;

    /// <summary>The existing gallery maximum, mirrored here so oversized pages clamp (never 400).</summary>
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 25;

    /// <summary>Matches the 200-character search limit the methods and reports endpoints enforce.</summary>
    public const int MaxSearchLength = 200;

    // The task columns' storage limits (AppDbContext), mirrored here so an over-long value is
    // refused with a precise message and advertised as maxLength in the tool schema. Without this
    // the value reaches SQL Server, which raises a truncation error that surfaces to the agent as
    // an opaque internal error. The browser never trips on it because its form fields are capped.
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxReferenceLinkLength = 2000;
    public const int MaxDataSourceLength = 1000;
    public const int MaxDataOutputLength = 1000;
    public const int MaxNotesLength = 1000;
    public const int MaxStepInstructionsLength = 2000;
    public const int MaxApproachLength = 100000;
    public const int MaxReportDescriptionLength = 4000;
    public const int MaxUrlLength = 2048;

    private readonly McpOptions _options;
    private readonly IReadOnlyList<McpToolDefinition> _all;

    public McpToolCatalog(McpOptions options)
    {
        _options = options;
        _all = BuildAll();
    }

    /// <summary>Capabilities the current policy allows this caller to see and use.</summary>
    public IReadOnlyList<McpToolDefinition> Visible =>
        VisibleFor(_options.PublicDemoEnabled);

    public IReadOnlyList<McpToolDefinition> VisibleFor(bool publicReadOnly) =>
        _all.Where(t => publicReadOnly ? IsPublicRead(t.Name) : !t.RequiresWrite || _options.WritesEnabled).ToList();

    public IReadOnlyList<McpToolDefinition> VisibleFor(CollectionRequestContext? context)
    {
        if (context?.Capabilities.IsPublicDemo != true)
            return Visible;

        return _all.Where(tool => IsPublicRead(tool.Name) ||
            tool.RequiresWrite && _options.PublicAvailable &&
            context.CollectionId == CollectionScope.Playground &&
            context.Capabilities.CanWrite).ToList();
    }

    /// <summary>
    /// Resolves a tool by name, honouring the same filter as <see cref="Visible"/>. Returns null
    /// both for a name that does not exist and for one that policy hides.
    /// </summary>
    public McpToolDefinition? Find(string name, bool publicReadOnly = false) =>
        VisibleFor(publicReadOnly || _options.PublicDemoEnabled).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    public McpToolDefinition? Find(string name, CollectionRequestContext? context) =>
        VisibleFor(context).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>Every definition regardless of policy — for tests and diagnostics only.</summary>
    internal IReadOnlyList<McpToolDefinition> All => _all;

    private static IReadOnlyList<McpToolDefinition> BuildAll() =>
    [
        new(SearchTasks,
            "Search tasks",
            "Search the gallery's reusable building blocks — agents, skills, plugins, prompts and " +
            "MCP tools — by free text, kind or the Microsoft AI surface they run in. Returns the " +
            "same results, in the same order, that the signed-in person would see when browsing.",
            SchemaObject(
                Properties(
                    ("q", SearchProperty("Free-text search over name, description and tags.")),
                    ("taskType", RegistryProperty(CatalogRegistry.Default.KindIds, "Restrict to one kind of task.")),
                    ("targetId", RegistryProperty(CatalogRegistry.Default.TargetIds, "Restrict to one target.")),
                    ("page", PageProperty()),
                    ("pageSize", PageSizeProperty())))),

        new(GetTask,
            "Get task",
            "Retrieve one task in full, including its content or reference link, its tags, its " +
            "linked AI method, and the workflows that use it.",
            SchemaObject(
                Properties(("id", IdProperty("Identifier of the task to retrieve."))),
                required: ["id"])),

        new(SearchWorkflows,
            "Search workflows",
            "Search the gallery's documented end-to-end approaches. A workflow links two or more " +
            "steps across Microsoft AI surfaces to solve a stated problem and reach a stated " +
            "outcome. Prefer this over inventing an approach when the organisation already has one.",
            SchemaObject(
                Properties(
                    ("q", SearchProperty("Free-text search over name, problem statement and desired outcome.")),
                    ("taskId", IdProperty("Only workflows that include this task as a step.")),
                    ("taskType", RegistryProperty(CatalogRegistry.Default.KindIds, "Only workflows with this task kind.")),
                    ("targetId", RegistryProperty(CatalogRegistry.Default.TargetIds, "Only workflows on this target.")),
                    ("page", PageProperty()),
                    ("pageSize", PageSizeProperty())))),

        new(GetWorkflow,
            "Get workflow",
            "Retrieve one workflow in full: its problem and outcome, its ordered steps with the " +
            "surface each runs in, and the AI method it is based on.",
            SchemaObject(
                Properties(("id", IdProperty("Identifier of the workflow to retrieve."))),
                required: ["id"])),

        new(SearchMethods,
            "Search AI methods",
            "Search the organisation's AI methods — the reusable ways of approaching a class of " +
            "problem that tasks and workflows are built on.",
            SchemaObject(
                Properties(
                    ("q", SearchProperty("Free-text search over title and summary.")),
                    ("page", PageProperty()),
                    ("pageSize", PageSizeProperty())))),

        new(GetMethod,
            "Get AI method",
            "Retrieve one AI method in full, including its complete written approach.",
            SchemaObject(
                Properties(("id", IdProperty("Identifier of the AI method to retrieve."))),
                required: ["id"])),

        new(SearchReports,
            "Search reports and AI apps",
            "Search the directory of reports and AI apps. This returns directory metadata and the " +
            "destination link only — the gallery never fetches or reproduces what is behind that link.",
            SchemaObject(
                Properties(
                    ("q", SearchProperty("Free-text search over name and description.")),
                    ("kind", EnumProperty<ReportLinkKind>("Restrict to reports or to AI apps.")),
                    ("page", PageProperty()),
                    ("pageSize", PageSizeProperty())))),

        new(GetReport,
            "Get report or AI app",
            "Retrieve one directory entry, including its destination URL. The destination itself is " +
            "not fetched, proxied or embedded.",
            SchemaObject(
                Properties(("id", IdProperty("Identifier of the directory entry to retrieve."))),
                required: ["id"])),

        new(CreateTask,
            "Create task",
            "Add a new reusable building block to the gallery, recorded against the signed-in " +
            "person exactly as if they had filled in the browser form. Supply at least one of " +
            "`referenceLink` or `inlineContent` — file attachments cannot be sent over MCP, so use " +
            "a link to the file instead. Ask the person to confirm the content before calling this.",
            SchemaObject(
                Properties(TaskFields(
                    ("name", TextProperty("Short name for the task.", MaxNameLength)),
                    ("description", TextProperty(
                        "What this task does and when to reach for it.", MaxDescriptionLength)))),
                required: ["name", "description", "taskType", "targetIds"]),
            RequiresWrite: true,
            Idempotent: false),

        new(UpdateTask,
            "Update task",
            "Change an existing task. Only the fields you supply are changed — anything you omit " +
            "keeps its current value, and an existing file attachment is always preserved. To clear " +
            "an optional field, pass an empty string for it. Pass `expectedLastEditedAt` from a " +
            "previous get_task so that an edit someone else made in the meantime is reported as a " +
            "conflict instead of being silently overwritten.",
            SchemaObject(
                Properties(TaskFields(
                    ("id", IdProperty("Identifier of the task to change.")),
                    ("name", TextProperty("Replacement name. Omit to keep the current one.", MaxNameLength)),
                    ("description", TextProperty(
                        "Replacement description. Omit to keep the current one.", MaxDescriptionLength)),
                    ("expectedLastEditedAt", TimestampProperty(
                        "The `lastEditedAt` you last saw for this task. If it no longer matches, " +
                        "the update is refused as a conflict and the current version is returned. " +
                        "Omitting this means last-write-wins.")))),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(DeleteTask,
            "Delete task",
            "Permanently remove a task from the gallery. This cannot be undone. If the task is used " +
            "by any workflow the call is refused and the using workflows are listed; re-run with " +
            "`confirm: true` only after the person has seen that list and agreed. Those workflows " +
            "keep the step, showing the task's last-known name.",
            SchemaObject(
                Properties(
                    ("id", IdProperty("Identifier of the task to delete.")),
                    ("confirm", new JsonObject
                    {
                        ["type"] = "boolean",
                        ["default"] = false,
                        ["description"] =
                            "Set to true to proceed even though workflows reference this task. " +
                            "Only set this after a person has approved the listed impact.",
                    })),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(CreateWorkflow,
            "Create workflow",
            "Add a new end-to-end workflow to the gallery, recorded against the signed-in person " +
            "exactly as if they had filled in the browser form. A workflow is an ordered sequence " +
            "of steps and needs at least two of them; each step either points at an existing task " +
            "(find one with search_tasks first) or is a manual step describing something a person " +
            "does. Ask the person to confirm the sequence before calling this.",
            SchemaObject(
                Properties(WorkflowFields(
                    ("name", TextProperty("Short name for the workflow.", MaxNameLength)),
                    ("problemStatement", TextProperty(
                        "The problem this workflow solves.", MaxDescriptionLength)),
                    ("desiredOutcome", TextProperty(
                        "What you have once it is done.", MaxDescriptionLength)))),
                required: ["name", "problemStatement", "desiredOutcome", "steps"]),
            RequiresWrite: true,
            Idempotent: false),

        new(UpdateWorkflow,
            "Update workflow",
            "Change an existing workflow. Only the fields you supply are changed — anything you " +
            "omit keeps its current value. `steps` is all-or-nothing: omit it to leave the " +
            "sequence exactly as it is, or supply the complete new sequence, which replaces the " +
            "old one outright. Pass `expectedLastEditedAt` from a previous get_workflow so that an " +
            "edit someone else made in the meantime is reported as a conflict instead of being " +
            "silently overwritten.",
            SchemaObject(
                Properties(WorkflowFields(
                    ("id", IdProperty("Identifier of the workflow to change.")),
                    ("name", TextProperty("Replacement name. Omit to keep the current one.", MaxNameLength)),
                    ("problemStatement", TextProperty(
                        "Replacement problem statement. Omit to keep the current one.", MaxDescriptionLength)),
                    ("desiredOutcome", TextProperty(
                        "Replacement desired outcome. Omit to keep the current one.", MaxDescriptionLength)),
                    ("expectedLastEditedAt", TimestampProperty(
                        "The `lastEditedAt` you last saw for this workflow. If it no longer " +
                        "matches, the update is refused as a conflict and the current version is " +
                        "returned. Omitting this means last-write-wins on the fields you supply; " +
                        "if you also omit `steps`, the existing sequence is guarded automatically " +
                        "so a concurrent edit to it cannot be silently reverted.")))),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(DeleteWorkflow,
            "Delete workflow",
            "Permanently remove a workflow and all of its steps from the gallery. This cannot be " +
            "undone and nothing else in the gallery guards it, so the call is refused until you " +
            "re-run it with `confirm: true` — do that only after the person has seen what will be " +
            "removed and agreed. The tasks the steps referred to are not affected.",
            SchemaObject(
                Properties(
                    ("id", IdProperty("Identifier of the workflow to delete.")),
                    ("confirm", new JsonObject
                    {
                        ["type"] = "boolean",
                        ["default"] = false,
                        ["description"] =
                            "Set to true to actually delete the workflow. Only set this after a " +
                            "person has approved the deletion.",
                    })),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(CreateMethod,
            "Create AI method",
            "Add a new AI method to the gallery — a reusable way of approaching a class of " +
            "problem, which tasks and workflows can then be built on. Recorded against the " +
            "signed-in person exactly as if they had filled in the browser form. Search first with " +
            "search_methods: methods are the gallery's most re-used content, and a near-duplicate " +
            "is worse than none. Ask the person to confirm the approach before calling this.",
            SchemaObject(
                Properties(
                    ("title", TextProperty("Short name for the method.", MaxNameLength)),
                    ("summary", TextProperty(
                        "What the method is for, in a sentence or two.", MaxDescriptionLength)),
                    ("approach", TextProperty(
                        "The method written out in full, as Markdown.", MaxApproachLength))),
                required: ["title", "summary", "approach"]),
            RequiresWrite: true,
            Idempotent: false),

        new(UpdateMethod,
            "Update AI method",
            "Change an existing AI method. Only the fields you supply are changed — anything you " +
            "omit keeps its current value. Note that `approach` is replaced wholesale, not " +
            "appended to, so send the complete text. Pass `expectedVersion` from a previous " +
            "get_method so that an edit someone else made in the meantime is reported as a " +
            "conflict; if you omit it, the version read at the start of this call is used, which " +
            "still protects against a concurrent edit.",
            SchemaObject(
                Properties(
                    ("id", IdProperty("Identifier of the AI method to change.")),
                    ("title", TextProperty("Replacement title. Omit to keep the current one.", MaxNameLength)),
                    ("summary", TextProperty(
                        "Replacement summary. Omit to keep the current one.", MaxDescriptionLength)),
                    ("approach", TextProperty(
                        "Replacement approach, in full. Omit to keep the current one.", MaxApproachLength)),
                    ("expectedVersion", VersionProperty(
                        "The `version` you last saw for this AI method. If it no longer matches, " +
                        "the update is refused as a conflict and nothing is written."))),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(DeleteMethod,
            "Delete AI method",
            "Permanently remove an AI method from the gallery. This cannot be undone. Only the " +
            "person who created it, or an administrator, may delete it. If any task or workflow " +
            "is still based on it the call is refused and those items are listed — they must be " +
            "unlinked first, as there is no override. The call is also refused until you re-run " +
            "it with `confirm: true`, which you should do only after the person has agreed.",
            SchemaObject(
                Properties(
                    ("id", IdProperty("Identifier of the AI method to delete.")),
                    ("confirm", new JsonObject
                    {
                        ["type"] = "boolean",
                        ["default"] = false,
                        ["description"] =
                            "Set to true to actually delete the method. Only set this after a " +
                            "person has approved the deletion.",
                    })),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(CreateReport,
            "Create report or AI app link",
            "Add a new entry to the directory of reports and AI apps. This records directory " +
            "metadata and a destination link only — the gallery never fetches, proxies or " +
            "reproduces what is behind that link, so describe the destination yourself rather " +
            "than expecting the gallery to read it. The URL must be an absolute https:// one.",
            SchemaObject(
                Properties(ReportFields(
                    ("name", TextProperty("Short name for the entry.", MaxNameLength)),
                    ("description", TextProperty(
                        "What this report or app is for, and who it is useful to.",
                        MaxReportDescriptionLength)))),
                required: ["name", "description", "url", "kind"]),
            RequiresWrite: true,
            Idempotent: false),

        new(UpdateReport,
            "Update report or AI app link",
            "Change an existing directory entry. Only the fields you supply are changed — anything " +
            "you omit keeps its current value. Pass `expectedVersion` from a previous get_report " +
            "so that an edit someone else made in the meantime is reported as a conflict; if you " +
            "omit it, the version read at the start of this call is used, which still protects " +
            "against a concurrent edit.",
            SchemaObject(
                Properties(ReportFields(
                    ("id", IdProperty("Identifier of the directory entry to change.")),
                    ("name", TextProperty("Replacement name. Omit to keep the current one.", MaxNameLength)),
                    ("description", TextProperty(
                        "Replacement description. Omit to keep the current one.",
                        MaxReportDescriptionLength)),
                    ("expectedVersion", VersionProperty(
                        "The `version` you last saw for this entry. If it no longer matches, the " +
                        "update is refused as a conflict and nothing is written.")))),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),

        new(DeleteReport,
            "Delete report or AI app link",
            "Permanently remove an entry from the directory of reports and AI apps. This cannot be " +
            "undone and nothing else in the gallery guards it, so the call is refused until you " +
            "re-run it with `confirm: true` — do that only after the person has agreed. Only the " +
            "directory entry is removed; the destination it pointed at is untouched.",
            SchemaObject(
                Properties(
                    ("id", IdProperty("Identifier of the directory entry to delete.")),
                    ("confirm", new JsonObject
                    {
                        ["type"] = "boolean",
                        ["default"] = false,
                        ["description"] =
                            "Set to true to actually delete the entry. Only set this after a " +
                            "person has approved the deletion.",
                    })),
                required: ["id"]),
            RequiresWrite: true,
            Destructive: true),
    ];

    /// <summary>
    /// The directory-entry fields shared by create and update, so the two tools cannot drift apart.
    /// The concurrency guard is not among them: there is no version to expect before the entry
    /// exists, and advertising an argument the create tool would reject is worse than repeating it.
    /// </summary>
    private static (string, JsonObject)[] ReportFields(params (string Name, JsonObject Schema)[] leading) =>
    [
        .. leading,
        ("url", new JsonObject
        {
            ["type"] = "string",
            ["format"] = "uri",
            ["maxLength"] = MaxUrlLength,
            ["description"] =
                "Absolute https:// link to the destination. http://, relative links, links " +
                "carrying embedded credentials and links containing whitespace or backslashes are " +
                "all refused.",
        }),
        ("kind", EnumProperty<ReportLinkKind>("Whether this entry is a report or an AI app.")),
    ];

    /// <summary>
    /// The workflow fields shared by create and update, so the two tools cannot drift apart. As
    /// with tasks, the leading arguments differ between them and are passed in.
    /// </summary>
    private static (string, JsonObject)[] WorkflowFields(params (string Name, JsonObject Schema)[] leading) =>
    [
        .. leading,
        ("steps", new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 2,
            ["items"] = StepSchema(),
            ["description"] =
                "The complete ordered sequence, at least two steps long. Supplying this replaces " +
                "every existing step, so send the whole sequence rather than just the ones you are " +
                "changing. Order in the array is the order of the steps.",
        }),
        ("methodId", new JsonObject
        {
            ["type"] = "string",
            ["description"] =
                "AI method this workflow is based on, as its identifier. Pass an empty string to " +
                "remove an existing link.",
        }),
    ];

    private static JsonObject StepSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new JsonObject
        {
            ["taskId"] = IdProperty(
                "The task this step runs. Required unless the step is a manual one. Use " +
                "search_tasks to find it — inventing an identifier fails the whole call."),
            ["isManualStep"] = new JsonObject
            {
                ["type"] = "boolean",
                ["default"] = false,
                ["description"] =
                    "True when a person does this step themselves rather than it being a task in " +
                    "the gallery. A manual step needs a manualStepTitle and no taskId.",
            },
            ["manualStepTitle"] = TextProperty(
                "What the person does at this step. Required for a manual step.", MaxNameLength),
            ["manualStepInstructions"] = TextProperty(
                "How they do it, if it needs saying.", MaxStepInstructionsLength),
            ["targetId"] = RegistryProperty(CatalogRegistry.Default.TargetIds, "Target belonging to the task. Null for manual steps.", nullable: true),
            ["notes"] = TextProperty(
                "Anything worth knowing about this step in this workflow.", MaxNotesLength),
            ["dataSource"] = TextProperty("What this step reads from.", MaxDataSourceLength),
            ["dataOutput"] = TextProperty("What this step produces.", MaxDataOutputLength),
        },
        ["required"] = new JsonArray("targetId"),
    };

    /// <summary>
    /// The task fields shared by create and update, so the two tools cannot drift apart. The
    /// leading arguments differ between them (create has no id, update's text is phrased as
    /// "replacement"), so they are passed in rather than duplicated here.
    /// </summary>
    private static (string, JsonObject)[] TaskFields(params (string Name, JsonObject Schema)[] leading) =>
    [
        .. leading,
        ("taskType", RegistryProperty(CatalogRegistry.Default.KindIds, "Artifact kind.")),
        ("targetIds", new JsonObject
        {
            ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 16, ["uniqueItems"] = true,
            ["description"] = "One to sixteen unique compatible target IDs.",
            ["items"] = RegistryProperty(CatalogRegistry.Default.TargetIds, "Supported target ID.")
        }),
        ("format", RegistryProperty(CatalogRegistry.Default.FormatIds.Concat(new[] { "" }), "Optional artifact format. Omit to preserve on update; send an empty string to clear.", nullable: true)),
        ("setup", TextProperty("Setup guidance.", 10000)),
        ("prerequisites", TextProperty("Prerequisites.", 10000)),
        ("tags", new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "string" },
            ["description"] =
                "Free-text tags used for grouping and search, one tag per array element. A tag " +
                "cannot itself contain a comma. Pass an empty array to remove all tags.",
        }),
        ("referenceLink", TextProperty(
            "Absolute http:// or https:// link to where the task actually lives.", MaxReferenceLinkLength)),
        ("inlineContent", TextProperty("The task's content held directly in the gallery, such as a prompt.")),
        ("dataSource", TextProperty("What this task reads from.", MaxDataSourceLength)),
        ("dataOutput", TextProperty("What this task produces.", MaxDataOutputLength)),
        ("methodId", new JsonObject
        {
            ["type"] = "string",
            ["description"] =
                "AI method this task is based on, as its identifier. Pass an empty string to remove " +
                "an existing link.",
        }),
    ];

    private static JsonObject TextProperty(string description, int? maxLength = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
        };

        if (maxLength is { } limit)
        {
            schema["maxLength"] = limit;
        }

        return schema;
    }

    private static JsonObject TimestampProperty(string description) => new()
    {
        ["type"] = "string",
        ["format"] = "date-time",
        ["description"] = description,
    };

    /// <summary>
    /// A concurrency token. AI methods and directory entries version with an opaque identifier
    /// rather than an edit timestamp, so their guard is a different argument from the one tasks
    /// and workflows use, and is named for what it is.
    /// </summary>
    private static JsonObject VersionProperty(string description) => new()
    {
        ["type"] = "string",
        ["format"] = "uuid",
        ["description"] = description,
    };

    private static JsonObject SchemaObject(JsonObject properties, string[]? required = null)
    {
        properties["collectionId"] = RegistryProperty(
            new[] { CollectionScope.PublicTechniques, CollectionScope.CuratedTools },
            "Required collection scope. Select exactly one collection; never inferred from the MCP endpoint URL.");
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            // Closed schemas make a typo a clear -32602 rather than a silently ignored argument.
            ["additionalProperties"] = false,
        };

        schema["required"] = new JsonArray((required ?? Array.Empty<string>()).Append("collectionId")
            .Select(r => (JsonNode)r!).ToArray());

        return schema;
    }

    private static JsonObject Properties(params (string Name, JsonObject Schema)[] properties)
    {
        var obj = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            obj[name] = schema;
        }

        return obj;
    }

    private static JsonObject IdProperty(string description) => new()
    {
        ["type"] = "string",
        ["format"] = "uuid",
        ["description"] = description,
    };

    private static JsonObject SearchProperty(string description) => new()
    {
        ["type"] = "string",
        ["maxLength"] = MaxSearchLength,
        ["description"] = description,
    };

    private static JsonObject PageProperty() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["default"] = 1,
        ["description"] = "1-based page number.",
    };

    private static JsonObject PageSizeProperty() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = MaxPageSize,
        ["default"] = DefaultPageSize,
        ["description"] = $"Results per page. Values above {MaxPageSize} are clamped to {MaxPageSize}.",
    };

    private static JsonObject RegistryProperty(IEnumerable<string> ids, string description, bool nullable = false) => new()
    {
        ["type"] = nullable ? new JsonArray("string", "null") : JsonValue.Create("string"),
        ["enum"] = new JsonArray(ids.Select(n => (JsonNode?)JsonValue.Create(n)).Concat(nullable ? new JsonNode?[] { null } : Array.Empty<JsonNode?>()).ToArray()),
        ["description"] = description,
    };

    private static JsonObject EnumProperty<TEnum>(string description) where TEnum : struct, Enum => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(Enum.GetNames<TEnum>().Select(n => (JsonNode)n!).ToArray()),
        ["description"] = description,
    };
}
