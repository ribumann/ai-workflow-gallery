namespace WorkflowGallery.Api.Models;

/// <summary>
/// Reserved-before-inference audit row for one <c>POST /api/ai/compose</c> call — used only for rate limiting
/// (FR-010) and usage monitoring (FR-014). Deliberately does not store the goal text itself, only
/// its length, to avoid persisting more user-typed content than needed for operational
/// troubleshooting.
/// </summary>
public class AiComposerRequestLog
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Set on create; the rate limiter counts rows in a rolling window from this.</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>Character count of the submitted goal text (monitoring only, never the text itself).</summary>
    public int GoalLength { get; set; }

    /// <summary>Whether a usable proposal was returned.</summary>
    public bool Success { get; set; }

    /// <summary>Wall-clock time of the model call, for latency monitoring (SC-001).</summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Short machine-readable reason when <see cref="Success"/> is false (e.g. "timeout",
    /// "rate_limited", "unusable_response") — never the raw user input.
    /// </summary>
    public string? FailureReason { get; set; }
}
