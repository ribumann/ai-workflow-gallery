namespace WorkflowGallery.Api.Models;

/// <summary>Global spend reservation, deliberately without identity, prompt, or proposal content.</summary>
public sealed class PublicComposerReservation
{
    public Guid Id { get; set; }
    public long RequestedAtUnixSeconds { get; set; }
    public long LeaseExpiresAtUnixSeconds { get; set; }
    public int ReservedTokens { get; set; }
    public bool Completed { get; set; }
}
