using System.Text.Json.Nodes;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// One self-describing capability (FR-006). The schema is authored as data rather than derived
/// from a CLR type so the advertised argument shape and the binding in
/// <see cref="McpArguments"/> stay in one readable place.
/// </summary>
/// <param name="Name">Stable tool name an agent calls.</param>
/// <param name="Title">Human-readable label shown by MCP clients.</param>
/// <param name="Description">What the tool is for, written for the model that chooses it.</param>
/// <param name="InputSchema">JSON Schema for the arguments object.</param>
/// <param name="RequiresWrite">
/// When true the capability is only advertised and callable when the selected connection and
/// deployment policy permit catalog writes.
/// </param>
/// <param name="Destructive">
/// Whether calling this tool can remove or overwrite content a person would miss. Clients surface
/// this to decide how loudly to confirm with the human before running it.
/// </param>
/// <param name="Idempotent">
/// Whether repeating an identical call leaves the gallery in the same state. Creates are not:
/// calling one twice produces two items (spec.md Edge Cases), and saying otherwise would invite a
/// client to retry a create it should have surfaced.
/// </param>
public sealed record McpToolDefinition(
    string Name,
    string Title,
    string Description,
    JsonObject InputSchema,
    bool RequiresWrite = false,
    bool Destructive = false,
    bool Idempotent = true)
{
    /// <summary>
    /// Projects the definition into the wire shape <c>tools/list</c> returns. Annotations tell a
    /// client whether this tool changes anything (<c>readOnlyHint</c>) and that it touches no
    /// external system beyond the gallery itself (<c>openWorldHint</c>) — the latter is the
    /// machine-readable form of FR-010's promise that linked destinations are never fetched.
    /// </summary>
    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["title"] = Title,
        ["description"] = Description,
        ["inputSchema"] = InputSchema.DeepClone(),
        ["annotations"] = new JsonObject
        {
            ["title"] = Title,
            ["readOnlyHint"] = !RequiresWrite,
            ["destructiveHint"] = Destructive,
            ["idempotentHint"] = Idempotent,
            ["openWorldHint"] = false,
        },
    };
}
