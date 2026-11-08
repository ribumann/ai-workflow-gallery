using System.Text.Json;
using System.Text.Json.Nodes;

namespace WorkflowGallery.Api.Services.Mcp;

/// <summary>
/// Raised when a tool's arguments cannot be bound. Surfaces as JSON-RPC <c>-32602</c>
/// (invalid params) rather than a tool error, because the call was never valid enough to run.
/// </summary>
public sealed class McpArgumentException(string message) : Exception(message);

/// <summary>
/// Typed, validating access to a <c>tools/call</c> arguments object.
///
/// <para>
/// <b>Why paging is clamped here.</b> The gallery's own endpoints disagree about oversized pages:
/// tasks and workflows silently clamp, methods and reports answer <c>400</c>. The spec's edge case
/// requires a clamp to the existing maximum, so normalising at this boundary is what makes all four
/// search tools behave alike without touching any browser-facing endpoint (FR-011). The maximum is
/// the existing one — this introduces no new limit.
/// </para>
/// </summary>
public sealed class McpArguments
{
    private readonly JsonObject _arguments;
    private readonly string _toolName;

    /// <summary>
    /// Prefix applied to every argument name that appears in an error message. Empty at the top
    /// level; for a nested object it is the path that reaches it — <c>steps[2].</c> — so an agent
    /// is told which step of a sequence it got wrong rather than just which field.
    /// </summary>
    private readonly string _path;

    public McpArguments(string toolName, JsonObject? arguments)
        : this(toolName, arguments, string.Empty)
    {
    }

    private McpArguments(string toolName, JsonObject? arguments, string path)
    {
        _toolName = toolName;
        _arguments = arguments ?? new JsonObject();
        _path = path;
    }

    private string Path(string name) => _path + name;

    /// <summary>
    /// Rejects arguments the tool does not declare. The schemas are closed
    /// (<c>additionalProperties: false</c>), so honouring that here turns a client's typo into an
    /// explicit error instead of a filter that silently did nothing.
    /// </summary>
    public void RejectUnknown(params string[] allowed)
    {
        var unknown = _arguments
            .Select(p => p.Key)
            .Where(k => !allowed.Contains(k, StringComparer.Ordinal))
            .Select(Path)
            .ToList();

        if (unknown.Count > 0)
        {
            throw new McpArgumentException(
                $"Tool '{_toolName}' does not accept the argument(s): {string.Join(", ", unknown)}.");
        }
    }

    /// <summary>A required GUID argument.</summary>
    public Guid RequiredId(string name)
    {
        var raw = OptionalString(name)
            ?? throw new McpArgumentException($"Tool '{_toolName}' requires the '{Path(name)}' argument.");

        return Guid.TryParse(raw, out var id)
            ? id
            : throw new McpArgumentException($"'{Path(name)}' must be a valid identifier (GUID). Received '{raw}'.");
    }

    /// <summary>An optional GUID argument, returned as its canonical string form or null.</summary>
    public string? OptionalId(string name)
    {
        var raw = OptionalString(name);
        if (raw is null)
        {
            return null;
        }

        return Guid.TryParse(raw, out var id)
            ? id.ToString()
            : throw new McpArgumentException($"'{Path(name)}' must be a valid identifier (GUID). Received '{raw}'.");
    }

    /// <summary>An optional free-text argument, trimmed, length-checked, empty treated as absent.</summary>
    public string? OptionalSearch(string name)
    {
        var raw = OptionalString(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (raw.Length > McpToolCatalog.MaxSearchLength)
        {
            throw new McpArgumentException(
                $"'{Path(name)}' must be {McpToolCatalog.MaxSearchLength} characters or fewer.");
        }

        return raw;
    }

    /// <summary>
    /// An optional enum argument, validated against the advertised values so an agent gets a
    /// precise error instead of a filter that was quietly dropped.
    /// </summary>
    public string? OptionalEnum<TEnum>(string name) where TEnum : struct, Enum
    {
        var raw = OptionalString(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed.ToString()
            : throw new McpArgumentException(
                $"'{Path(name)}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}. Received '{raw}'.");
    }

    /// <summary>An enum argument that must be present.</summary>
    public string RequiredEnum<TEnum>(string name) where TEnum : struct, Enum =>
        OptionalEnum<TEnum>(name)
        ?? throw new McpArgumentException($"Tool '{_toolName}' requires the '{Path(name)}' argument.");

    public string? RegistryId(string name, IEnumerable<string> allowed)
    {
        var raw = OptionalString(name);
        if (raw is null) return null;
        return allowed.Contains(raw, StringComparer.Ordinal) ? raw
            : throw new McpArgumentException($"'{Path(name)}' must be one of: {string.Join(", ", allowed)}.");
    }

    public string RequiredCollectionId()
    {
        var id = OptionalString("collectionId");
        return CollectionScope.IsValid(id) ? id!
            : throw new McpArgumentException("'collectionId' is required and must be public-techniques, curated-tools or playground.");
    }

    public List<string>? TargetIds()
    {
        if (_arguments["targetIds"] is null) return null;
        if (_arguments["targetIds"] is not JsonArray array || array.Count is < 1 or > 16 ||
            array.Any(x => x is null || x.GetValueKind() != JsonValueKind.String))
            throw new McpArgumentException("'targetIds' must be an array of 1 to 16 unique registry IDs.");
        var ids = array.Select(x => x!.GetValue<string>()).ToList();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(x => !CatalogRegistry.Default.IsTarget(x)))
            throw new McpArgumentException("'targetIds' contains duplicate or unknown IDs.");
        return ids;
    }

    /// <summary>
    /// Page number, clamped to at least 1 and to whatever keeps the resulting row offset inside
    /// <see cref="int.MaxValue"/>.
    ///
    /// <para>
    /// The upper clamp is not theoretical. The delegated endpoints disagree about huge pages:
    /// Methods/Reports return 400, Workflows computes <c>(page - 1) * pageSize</c> unchecked and
    /// overflows into a negative SQL <c>OFFSET</c>, and Tasks pages in memory, where a negative
    /// <c>Skip</c> silently yields page 1 while still reporting the requested page — the worst
    /// outcome of the three, because an agent is confidently shown the wrong rows. Clamping here
    /// gives all four search tools one predictable contract without touching any browser-facing
    /// endpoint (FR-011).
    /// </para>
    /// </summary>
    public int Page()
    {
        var requested = Math.Max(OptionalInt("page") ?? 1, 1);
        var maxPage = (int)Math.Min(int.MaxValue, (long)(int.MaxValue / PageSize()) + 1);
        return Math.Min(requested, maxPage);
    }

    /// <summary>Page size, clamped into the existing 1..100 range.</summary>
    public int PageSize() =>
        Math.Clamp(OptionalInt("pageSize") ?? McpToolCatalog.DefaultPageSize, 1, McpToolCatalog.MaxPageSize);

    /// <summary>
    /// Whether the caller supplied a usable value for this argument. This is the distinction an
    /// update tool turns on: an unsupplied argument means "leave it as it is", whereas an
    /// explicitly blank one means "clear it".
    ///
    /// <para>
    /// A JSON <c>null</c> counts as unsupplied, not as a clear. Language-model clients routinely
    /// emit <c>"field": null</c> for a parameter they simply had no value for, and reading that as
    /// "remove what is there" would let a narrow edit quietly delete content someone curated. The
    /// empty string is the one deliberate way to clear a field, for every field alike.
    /// </para>
    /// </summary>
    public bool Has(string name) =>
        _arguments[name] is { } node && node.GetValueKind() != JsonValueKind.Null;

    /// <summary>
    /// A text argument, trimmed. Returns null when absent or when the caller supplied an empty or
    /// whitespace-only value — which the gallery's own write endpoints treat as "no value".
    /// </summary>
    public string? Text(string name, int? maxLength = null)
    {
        var raw = OptionalString(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (maxLength is { } limit && raw.Length > limit)
        {
            throw new McpArgumentException($"'{Path(name)}' must be {limit} characters or fewer.");
        }

        return raw;
    }

    /// <summary>A text argument that must be present and non-blank.</summary>
    public string RequiredText(string name, int? maxLength = null) =>
        Text(name, maxLength)
        ?? throw new McpArgumentException($"Tool '{_toolName}' requires a non-empty '{Path(name)}' argument.");

    /// <summary>
    /// An array-of-strings argument, blank entries dropped and duplicates left to the gallery's own
    /// handling. A bare string is accepted as a single-element array, because several MCP clients
    /// collapse one-item lists.
    /// </summary>
    public List<string>? StringArray(string name)
    {
        var node = _arguments[name];
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.String)
        {
            var single = node.GetValue<string>().Trim();
            return single.Length == 0 ? [] : [single];
        }

        if (node is not JsonArray array)
        {
            throw new McpArgumentException($"'{Path(name)}' must be an array of strings.");
        }

        var values = new List<string>();
        foreach (var element in array)
        {
            if (element is null || element.GetValueKind() == JsonValueKind.Null)
            {
                continue;
            }

            if (element.GetValueKind() != JsonValueKind.String)
            {
                throw new McpArgumentException($"'{Path(name)}' must be an array of strings.");
            }

            var value = element.GetValue<string>().Trim();
            if (value.Length > 0)
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// The tags argument. Identical to <see cref="StringArray"/> except that a comma inside a tag
    /// is refused.
    ///
    /// <para>
    /// The comma is a reserved delimiter: tags are stored joined by commas and split back apart on
    /// read, and neither the storage converter nor the endpoint's own normalisation guards it. The
    /// browser cannot produce such a tag because its editor splits the typed input on commas before
    /// submitting, so this is a hazard only an API caller can reach. Left unchecked, one tag is
    /// silently stored and read back as two — the second with a leading space that matches nothing
    /// else — and every later edit re-sends the split pair as though it had been curated.
    /// </para>
    /// </summary>
    public List<string>? TagArray(string name)
    {
        var tags = StringArray(name);

        if (tags?.FirstOrDefault(t => t.Contains(',', StringComparison.Ordinal)) is { } offending)
        {
            throw new McpArgumentException(
                $"'{Path(name)}' entries cannot contain a comma — it separates one tag from the next. " +
                $"Pass each tag as its own array element instead. Received '{offending}'.");
        }

        return tags;
    }

    /// <summary>
    /// An array-of-objects argument, each element returned as its own <see cref="McpArguments"/> so
    /// nested fields get the same validation — and the same error vocabulary — as top-level ones,
    /// with the path naming the element that was wrong.
    /// </summary>
    public List<McpArguments>? ObjectArray(string name)
    {
        var node = _arguments[name];
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        if (node is not JsonArray array)
        {
            throw new McpArgumentException($"'{Path(name)}' must be an array.");
        }

        var elements = new List<McpArguments>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject element)
            {
                throw new McpArgumentException($"'{Path(name)}[{i}]' must be an object.");
            }

            elements.Add(new McpArguments(_toolName, element, $"{Path(name)}[{i}]."));
        }

        return elements;
    }

    /// <summary>
    /// An ISO-8601 timestamp argument, used for the optimistic-concurrency guards. Parsed as UTC so
    /// a client that sends an offset is compared against stored time on the same terms the browser
    /// gets, rather than silently drifting by the offset and reading as a spurious conflict.
    /// </summary>
    public DateTime? OptionalTimestamp(string name)
    {
        var raw = OptionalString(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTime.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : throw new McpArgumentException(
                $"'{Path(name)}' must be an ISO-8601 timestamp, for example 2026-01-31T09:15:00Z. Received '{raw}'.");
    }

    /// <summary>A boolean argument, defaulting to false. A "true"/"false" string is accepted too.</summary>
    public bool OptionalBool(string name)
    {
        var node = _arguments[name];
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return false;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(node.GetValue<string>(), out var parsed) => parsed,
            _ => throw new McpArgumentException($"'{Path(name)}' must be true or false."),
        };
    }

    private int? OptionalInt(string name)
    {
        var node = _arguments[name];
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        // Accept a numeric string too: several MCP clients stringify every argument value.
        return node.GetValueKind() switch
        {
            JsonValueKind.Number when node.AsValue().TryGetValue<int>(out var i) => i,
            JsonValueKind.Number when node.AsValue().TryGetValue<double>(out var d) => (int)Math.Round(d),
            JsonValueKind.String when int.TryParse(node.GetValue<string>(), out var parsed) => parsed,
            _ => throw new McpArgumentException($"'{Path(name)}' must be an integer."),
        };
    }

    private string? OptionalString(string name)
    {
        var node = _arguments[name];
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : throw new McpArgumentException($"'{Path(name)}' must be a string.");
    }
}
