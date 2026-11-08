using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Data;

/// <summary>
/// EF Core database context wiring up the catalog relationships:
/// Task → WorkflowStep (ON DELETE SET NULL), Workflow → WorkflowStep (cascade delete),
/// unique directory-backed user identity.
/// </summary>
public class AppDbContext : DbContext
{
    public string CollectionId { get; set; } = "public-techniques";
    public bool PublicOnly { get; set; }
    public bool PublicDemoRequest { get; set; }
    public bool PublicCollectionWriteAllowed { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampCollections();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampCollections();
        return base.SaveChanges();
    }

    private void StampCollections()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (entries.Length == 0) return;
        if (PublicDemoRequest && (!PublicCollectionWriteAllowed || CollectionId != CollectionScope.Playground))
            throw new InvalidOperationException("Public catalog writes are restricted to the enabled Playground collection.");

        foreach (var entry in entries.Where(e => e.Metadata.FindProperty("CollectionId") is not null))
        {
            var property = entry.Property("CollectionId");
            if (entry.State == EntityState.Added)
            {
                property.CurrentValue = CollectionId;
                continue;
            }

            if (PublicDemoRequest &&
                (!string.Equals(property.OriginalValue as string, CollectionScope.Playground, StringComparison.Ordinal) ||
                 !string.Equals(property.CurrentValue as string, CollectionScope.Playground, StringComparison.Ordinal)))
                throw new InvalidOperationException("Public writes cannot modify records outside the Playground collection.");

            if (!Equals(property.OriginalValue, property.CurrentValue))
                throw new InvalidOperationException("A catalog record cannot be reassigned to another collection.");
        }

        if (PublicDemoRequest && entries.Any(e => e.Entity is User && e.State != EntityState.Unchanged))
            throw new InvalidOperationException("Public catalog writes cannot change directory identities.");
    }
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Rating> Ratings => Set<Rating>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<AiComposerRequestLog> AiComposerRequestLogs => Set<AiComposerRequestLog>();
    public DbSet<PublicComposerReservation> PublicComposerReservations => Set<PublicComposerReservation>();
    public DbSet<ReportLink> ReportLinks => Set<ReportLink>();
    public DbSet<AiMethod> AiMethods => Set<AiMethod>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PublicComposerReservation>().HasIndex(r => r.RequestedAtUnixSeconds);
        modelBuilder.Entity<PublicComposerReservation>().HasIndex(r => new { r.Completed, r.LeaseExpiresAtUnixSeconds });
        modelBuilder.Entity<CatalogSchemaMarker>().Property(x => x.Version).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<CatalogSchemaMarker>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<TaskItem>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_Tasks"));
        modelBuilder.Entity<Workflow>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_Workflows"));
        modelBuilder.Entity<WorkflowStep>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_WorkflowSteps"));
        modelBuilder.Entity<AiMethod>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_AiMethods"));
        modelBuilder.Entity<ReportLink>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_ReportLinks"));
        modelBuilder.Entity<Comment>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_Comments"));
        modelBuilder.Entity<Rating>().ToTable(table => table.HasTrigger("TR_PlaygroundGuard_Ratings"));
        modelBuilder.Entity<WorkflowGallery.Api.Services.Catalog.CatalogImportReceipt>(entity =>
        {
            entity.HasKey(x => new { x.CollectionId, x.DatasetId });
            entity.Property(x => x.CollectionId).HasMaxLength(64);
            entity.Property(x => x.DatasetId).HasMaxLength(200);
            entity.Property(x => x.ContentHash).HasMaxLength(64);
            entity.Property(x => x.StateHash).HasMaxLength(64);
        });
        modelBuilder.Entity<TaskItem>().HasQueryFilter(x => x.CollectionId == CollectionId && (!PublicOnly || x.IsPublished));
        modelBuilder.Entity<Workflow>().HasQueryFilter(x => x.CollectionId == CollectionId && (!PublicOnly || x.IsPublished));
        modelBuilder.Entity<AiMethod>().HasQueryFilter(x => x.CollectionId == CollectionId && (!PublicOnly || x.IsPublished));
        modelBuilder.Entity<ReportLink>().HasQueryFilter(x => x.CollectionId == CollectionId && (!PublicOnly || x.IsPublished));
        modelBuilder.Entity<WorkflowStep>().HasQueryFilter(x => x.CollectionId == CollectionId &&
            x.Workflow != null && x.Workflow.CollectionId == CollectionId && (!PublicOnly || x.Workflow.IsPublished));
        modelBuilder.Entity<Comment>().HasQueryFilter(x => x.CollectionId == CollectionId);
        modelBuilder.Entity<Rating>().HasQueryFilter(x => x.CollectionId == CollectionId);
        foreach (var type in new[] { typeof(Comment), typeof(Rating) })
        {
            modelBuilder.Entity(type).Property("CollectionId").HasMaxLength(64).IsRequired();
            modelBuilder.Entity(type).HasIndex("CollectionId", "SubjectType", "SubjectId");
        }
        foreach (var type in new[] { typeof(TaskItem), typeof(Workflow), typeof(AiMethod), typeof(ReportLink) })
        {
            modelBuilder.Entity(type).Property("CollectionId").HasMaxLength(64).IsRequired();
            modelBuilder.Entity(type).Property<bool>("IsPublished").HasDefaultValue(false);
            modelBuilder.Entity(type).HasIndex("CollectionId", "Id");
            modelBuilder.Entity(type).HasIndex("CollectionId", "IsPublished");
        }

        modelBuilder.Entity<AiMethod>(entity =>
        {
            entity.Property(m => m.Title).HasMaxLength(200).IsRequired();
            entity.Property(m => m.Summary).HasMaxLength(2000).IsRequired();
            entity.Property(m => m.Approach).HasMaxLength(100000).IsRequired();
            entity.Property(m => m.Version).IsConcurrencyToken();
            entity.Property(m => m.CreatedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(m => m.LastEditedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasIndex(m => new { m.Title, m.Id });
            entity.HasOne(m => m.CreatedByUser).WithMany()
                .HasForeignKey(m => m.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(m => m.LastEditedByUser).WithMany()
                .HasForeignKey(m => m.LastEditedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReportLink>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Description).HasMaxLength(4000).IsRequired();
            entity.Property(r => r.Url).HasMaxLength(2048).IsRequired();
            entity.Property(r => r.Version).IsConcurrencyToken();
            // SQL datetime2 loses DateTime.Kind; keep the UTC API contract on materialization.
            entity.Property(r => r.CreatedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(r => r.LastEditedAt).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.HasIndex(r => new { r.Kind, r.Name });
            entity.HasOne(r => r.CreatedByUser).WithMany()
                .HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(r => r.LastEditedByUser).WithMany()
                .HasForeignKey(r => r.LastEditedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => new { u.EntraTenantId, u.EntraObjectId }).IsUnique();
            entity.HasIndex(u => u.IsSandboxActor).IsUnique().HasFilter("[IsSandboxActor] = 1");
            entity.Property(u => u.EntraTenantId).HasMaxLength(36);
            entity.Property(u => u.EntraObjectId).HasMaxLength(100);
            entity.Property(u => u.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            // The table itself is renamed Artifacts -> Tasks (issue #76), but the PK/FK/index
            // identifiers are pinned to their pre-rename names: SQL Server constraint names don't
            // need to match the table name, and keeping them stable lets the accompanying
            // migration stay a minimal set of Rename* operations instead of also renaming every
            // PK/FK/index (which EF's naming convention would otherwise recompute from "Tasks").
            entity.HasKey(a => a.Id).HasName("PK_Artifacts");

            entity.Property(a => a.Name).HasMaxLength(200).IsRequired();
            entity.Property(a => a.Description).HasMaxLength(2000).IsRequired();
            entity.Property(a => a.TaskType).HasMaxLength(80).IsRequired();
            entity.Property(a => a.Format).HasMaxLength(80);
            entity.Property(a => a.Setup).HasMaxLength(10000);
            entity.Property(a => a.Prerequisites).HasMaxLength(10000);
            entity.Property(a => a.TargetIds)
                .HasConversion(
                    ids => string.Join(',', ids),
                    value => value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
                    ids => ids.ToList()));
            entity.Property(a => a.ReferenceLink).HasMaxLength(2000);
            entity.Property(a => a.AttachedFileBlobPath).HasMaxLength(500);
            entity.Property(a => a.AttachedFileName).HasMaxLength(260);
            entity.Property(a => a.DataSource).HasMaxLength(1000);
            entity.Property(a => a.DataOutput).HasMaxLength(1000);
            entity.HasOne(a => a.Method).WithMany()
                .HasForeignKey(a => a.MethodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(a => a.MethodId);

            // Tags stored as a delimited string column; the list-shaped property is exposed via a
            // value converter so the domain model keeps a simple List<string>.
            entity.Property(a => a.Tags)
                .HasConversion(
                    tags => string.Join(',', tags),
                    value => string.IsNullOrEmpty(value)
                        ? new List<string>()
                        : value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                    tags => tags.Aggregate(0, (hash, tag) => HashCode.Combine(hash, tag.GetHashCode())),
                    tags => tags.ToList()));

            entity.HasOne(a => a.CreatedByUser)
                .WithMany(u => u.CreatedTasks)
                .HasForeignKey(a => a.CreatedByUserId)
                .HasConstraintName("FK_Artifacts_Users_CreatedByUserId")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.LastEditedByUser)
                .WithMany(u => u.LastEditedTasks)
                .HasForeignKey(a => a.LastEditedByUserId)
                .HasConstraintName("FK_Artifacts_Users_LastEditedByUserId")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.CreatedByUserId).HasDatabaseName("IX_Artifacts_CreatedByUserId");
            entity.HasIndex(a => a.LastEditedByUserId).HasDatabaseName("IX_Artifacts_LastEditedByUserId");
        });

        modelBuilder.Entity<Workflow>(entity =>
        {
            entity.Property(w => w.Name).HasMaxLength(200).IsRequired();
            entity.Property(w => w.ProblemStatement).HasMaxLength(2000).IsRequired();
            entity.Property(w => w.DesiredOutcome).HasMaxLength(2000).IsRequired();
            entity.HasOne(w => w.Method).WithMany()
                .HasForeignKey(w => w.MethodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(w => w.MethodId);

            entity.HasOne(w => w.CreatedByUser)
                .WithMany(u => u.CreatedWorkflows)
                .HasForeignKey(w => w.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(w => w.LastEditedByUser)
                .WithMany(u => u.LastEditedWorkflows)
                .HasForeignKey(w => w.LastEditedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.Property(s => s.CollectionId).HasMaxLength(64).IsRequired();
            entity.HasIndex(s => s.CollectionId);
            entity.Property(s => s.TaskNameSnapshot).HasMaxLength(200);
            entity.Property(s => s.Notes).HasMaxLength(1000);
            entity.Property(s => s.ManualStepTitle).HasMaxLength(200);
            entity.Property(s => s.ManualStepInstructions).HasMaxLength(2000);
            entity.Property(s => s.DataSource).HasMaxLength(1000);
            entity.Property(s => s.DataOutput).HasMaxLength(1000);

            // Unique per-workflow sequence order (FR-008).
            entity.HasIndex(s => new { s.WorkflowId, s.SequenceOrder }).IsUnique();

            // Workflow → WorkflowStep: cascade delete (deleting a Workflow removes its steps).
            entity.HasOne(s => s.Workflow)
                .WithMany(w => w.Steps)
                .HasForeignKey(s => s.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Task → WorkflowStep: ON DELETE SET NULL (FR-010) — the step row survives with
            // TaskId = null and the snapshot fields populated by the delete operation. FK/index
            // names are pinned to their pre-rename values (see TaskItem config above).
            entity.HasOne(s => s.Task)
                .WithMany(a => a.WorkflowSteps)
                .HasForeignKey(s => s.TaskId)
                .HasConstraintName("FK_WorkflowSteps_Artifacts_ArtifactId")
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(s => s.TaskId).HasDatabaseName("IX_WorkflowSteps_ArtifactId");
        });

        modelBuilder.Entity<Rating>(entity =>
        {
            entity.Property(r => r.DeviceId).HasMaxLength(100).IsRequired();

            // One vote per device per subject (issue #60) — upserted/toggled, never accumulated.
            entity.HasIndex(r => new { r.CollectionId, r.SubjectType, r.SubjectId, r.DeviceId }).IsUnique();
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.Property(c => c.Body).HasMaxLength(1000).IsRequired();

            // Fast lookup for "list comments on this task/workflow", newest first.
            entity.HasIndex(c => new { c.CollectionId, c.SubjectType, c.SubjectId });

            entity.HasOne(c => c.CreatedByUser)
                .WithMany()
                .HasForeignKey(c => c.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AiComposerRequestLog>(entity =>
        {
            entity.Property(l => l.FailureReason).HasMaxLength(200);

            // Rate-limiter query shape: COUNT(*) WHERE UserId = @user AND RequestedAt >= @windowStart
            // for request-log rate-limit queries.
            entity.HasIndex(l => new { l.UserId, l.RequestedAt });

            entity.HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
