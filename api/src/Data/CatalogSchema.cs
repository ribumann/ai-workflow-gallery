using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace WorkflowGallery.Api.Data;

public sealed class CatalogSchemaMarker
{
    public int Id { get; set; }
    public required string Version { get; set; }
}

public static class CatalogSchema
{
    public const string CurrentVersion = "white-label-catalog-v4-playground-writes";
    private static readonly HashSet<string> CompatibleVersions = new(StringComparer.Ordinal)
    {
        CurrentVersion,
        "white-label-catalog-v2-public-composer"
    };

    public static string GenerateCreateScript(AppDbContext db)
    {
        var version = CurrentVersion.Replace("'", "''", StringComparison.Ordinal);
        var insert = db.Database.IsSqlServer()
            ? $"INSERT INTO [CatalogSchemaMarker] ([Id], [Version]) VALUES (1, N'{version}');\nGO\n"
            : $"INSERT INTO \"CatalogSchemaMarker\" (\"Id\", \"Version\") VALUES (1, '{version}');\n";
        return db.Database.GenerateCreateScript() + "\n" + insert;
    }

    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(ct)) await creator.CreateAsync(ct);
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (db.Database.IsSqlServer())
            {
                using var locking = connection.CreateCommand();
                locking.Transaction = transaction.GetDbTransaction();
                locking.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'CatalogSchema.Initialize', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; SELECT @result;";
                if (Convert.ToInt32(await locking.ExecuteScalarAsync(ct)) < 0)
                    throw new InvalidOperationException("Another initializer is using this database.");
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = db.Database.IsSqlServer()
                ? "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0"
                : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != 0)
                throw new InvalidOperationException("Catalog initialization requires an empty database. Existing databases are never reset.");
            await creator.CreateTablesAsync(ct);
            db.Set<CatalogSchemaMarker>().Add(new() { Id = 1, Version = CurrentVersion });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public static async Task VerifyAsync(AppDbContext db, CancellationToken ct = default)
    {
        try
        {
            var marker = await db.Set<CatalogSchemaMarker>().AsNoTracking().SingleOrDefaultAsync(ct);
            if (marker?.Id != 1 || marker.Version is null || !CompatibleVersions.Contains(marker.Version))
                throw new InvalidOperationException("Incompatible catalog schema.");

            // Validate only the fields guaranteed by the marker's compatible schema family.
            await db.Tasks.IgnoreQueryFilters().AsNoTracking().Select(t => new
                { t.CollectionId, t.IsPublished, t.TaskType, t.TargetIds, t.Format, t.Setup, t.Prerequisites }).Take(1).ToListAsync(ct);
            await db.Workflows.IgnoreQueryFilters().AsNoTracking().Select(w => new { w.CollectionId, w.IsPublished }).Take(1).ToListAsync(ct);
            await db.AiMethods.IgnoreQueryFilters().AsNoTracking().Select(m => new { m.CollectionId, m.IsPublished }).Take(1).ToListAsync(ct);
            await db.ReportLinks.IgnoreQueryFilters().AsNoTracking().Select(r => new { r.CollectionId, r.IsPublished }).Take(1).ToListAsync(ct);
            await db.PublicComposerReservations.AsNoTracking().Select(r => new
                { r.Id, r.RequestedAtUnixSeconds, r.LeaseExpiresAtUnixSeconds, r.ReservedTokens, r.Completed }).Take(1).ToListAsync(ct);

            if (string.Equals(marker.Version, CurrentVersion, StringComparison.Ordinal))
            {
                await db.WorkflowSteps.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.CollectionId, s.TargetId, s.TaskTypeSnapshot }).Take(1).ToListAsync(ct);
                await db.Comments.IgnoreQueryFilters().AsNoTracking().Select(c => new { c.CollectionId, c.SubjectType, c.SubjectId }).Take(1).ToListAsync(ct);
                await db.Ratings.IgnoreQueryFilters().AsNoTracking().Select(r => new { r.CollectionId, r.SubjectType, r.SubjectId }).Take(1).ToListAsync(ct);
                await db.Users.AsNoTracking().Select(u => u.IsSandboxActor).Take(1).ToListAsync(ct);
            }
            else
            {
                await db.WorkflowSteps.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.TargetId, s.TaskTypeSnapshot }).Take(1).ToListAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Catalog database is uninitialized or incompatible. Use --initialize-catalog only with a new empty database; legacy databases are not migrated.", ex);
        }
    }
}
