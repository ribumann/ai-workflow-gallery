using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;

namespace WorkflowGallery.Api.Services;

/// <summary>Anonymous proposals only; no identity creation, user audit rows, or catalog writes.</summary>
public sealed class PublicComposer(
    AppDbContext db, PublicComposerSettings settings, IPublicComposerBudget budget,
    IAiComposerService model, ILogger<PublicComposer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<IActionResult> ComposeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!settings.OptedIn) return PublicDemoPolicy.Denied();
        if (!settings.Ready) return Error(503, "composer_unavailable");
        if (!PublicComposerSettings.IsProposalRequest(request) ||
            !CollectionScope.IsValid(request.Query["collectionId"].ToString()))
            return Error(400, "An explicit valid collectionId is required.");
        db.CollectionId = request.Query["collectionId"].ToString();
        db.PublicOnly = true;
        ComposeRequestDto? dto;
        try
        {
            dto = await BoundedJsonBody.ReadAsync<ComposeRequestDto>(request, 8192, JsonOptions, cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413) { return Error(413, "Request body must be 8192 bytes or fewer."); }
        catch (JsonException) { return Error(400, "Only a goal is accepted; catalog context and IDs are selected by the server."); }
        if (string.IsNullOrWhiteSpace(dto?.Goal) || dto.Goal.Length > settings.MaxGoalCharacters)
            return Error(400, $"goal must contain 1-{settings.MaxGoalCharacters} characters.");

        IReadOnlyList<CatalogTaskSnapshot> candidates;
        try
        {
            var catalog = await db.Tasks.AsNoTracking().Where(t => t.IsPublished && t.CollectionId == db.CollectionId)
                .Select(t => new CatalogTaskSnapshot(t.Id, t.Name, t.Description, t.TaskType, t.TargetIds, t.Tags, t.Format))
                .ToListAsync(cancellationToken);
            candidates = CatalogCandidateSelector.SelectPublic(dto.Goal, catalog, settings.MaxContextBytes);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            logger.LogError("Public Composer catalog unavailable ({ErrorType}).", ex.GetType().Name);
            return Error(503, "composer_unavailable");
        }
        if (candidates.Count == 0) return Error(422, "No relevant published tasks in this collection. Try a more specific goal.");

        var reservation = await budget.CheckAsync(reserve: true, cancellationToken);
        if (!reservation.Allowed || reservation.ReservationId is null)
            return BudgetError(request, reservation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.ModelTimeoutSeconds));
        try
        {
            var response = await model.ComposeAsync(dto.Goal.Trim(), candidates, timeout.Token);
            // Validate at this boundary too: only server-approved IDs/targets may leave the service.
            if (response.Steps.Any(s => !candidates.Any(c => c.Id == s.TaskId &&
                    c.TargetIds.Contains(s.TargetId, StringComparer.Ordinal))))
                throw new AiComposerUnavailableException("Proposal references context outside this request.");
            // Recheck publication after inference in case an owner withdrew an item meanwhile.
            var ids = candidates.Select(s => s.Id).ToArray();
            if (await db.Tasks.AsNoTracking().Where(t => t.CollectionId == db.CollectionId && t.IsPublished && ids.Contains(t.Id))
                .CountAsync(cancellationToken) != ids.Length)
                throw new AiComposerUnavailableException("Proposal context changed during inference.");
            if (!await budget.CompleteAsync(reservation.ReservationId.Value, cancellationToken))
                return Error(503, "composer_unavailable");
            return new OkObjectResult(response);
        }
        catch (Exception ex) when (ex is AiComposerUnavailableException or OperationCanceledException or System.Data.Common.DbException)
        {
            logger.LogWarning("Public Composer failed ({ErrorType}); reservation retained.", ex.GetType().Name);
            return Error(502, "Could not get a proposal from the AI model. The request remains charged to the shared budget.");
        }
    }

    private static IActionResult BudgetError(HttpRequest request, PublicComposerBudgetResult result)
    {
        if (result.State is "quota_exhausted" or "busy")
        {
            request.HttpContext.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new ObjectResult(new { error = result.State, retryAfterSeconds = result.RetryAfterSeconds }) { StatusCode = 429 };
        }
        return Error(503, "composer_unavailable");
    }
    private static IActionResult Error(int status, string message) => new ObjectResult(new { error = message }) { StatusCode = status };
}
