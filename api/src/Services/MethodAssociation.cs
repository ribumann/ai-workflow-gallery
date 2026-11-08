using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Models;

namespace WorkflowGallery.Api.Services;

internal static class MethodAssociation
{
    internal const string MissingMessage = "The selected AI method no longer exists. Select another method or clear the selection.";

    internal static async Task<(bool Specified, AiMethod? Method, string? Error)> ResolveAsync(
        AppDbContext db, JsonElement value, CancellationToken cancellationToken)
    {
        // Undefined is an omitted property; JSON null is an explicit clear.
        if (value.ValueKind == JsonValueKind.Undefined) return (false, null, null);
        if (value.ValueKind == JsonValueKind.Null) return (true, null, null);
        if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var id) || id == Guid.Empty)
            return (true, null, "MethodId must be a nonempty GUID or null.");

        var method = await db.AiMethods.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        return method is null ? (true, null, MissingMessage) : (true, method, null);
    }

    internal static MethodRefDto? ToDto(AiMethod? method) =>
        method is null ? null : new(method.Id, method.Title, method.Summary);

    internal static bool IsReferenceViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql &&
        sql.Errors.Cast<SqlError>().Any(error => error.Number == 547 &&
            (error.Message.Contains("FK_Tasks_AiMethods_MethodId", StringComparison.Ordinal) ||
             error.Message.Contains("FK_Workflows_AiMethods_MethodId", StringComparison.Ordinal)));
}
