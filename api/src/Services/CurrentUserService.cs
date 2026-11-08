using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Services;

public interface ICurrentUserService
{
    Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default);
}

public sealed class CurrentUserService(AppDbContext dbContext) : ICurrentUserService
{
    public async Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        var policy = PublicDemoPolicy.For(request);
        if (policy.Enabled)
        {
            var context = CollectionRequestContext.For(request);
            if (context is null || context.CollectionId != CollectionScope.Playground || !context.Capabilities.CanWrite)
                return null;
            var actor = await dbContext.Users.SingleOrDefaultAsync(
                       user => user.Id == SandboxActorId && user.IsSandboxActor, cancellationToken)
                   ?? throw new InvalidOperationException("The provisioned Playground sandbox actor is missing.");
            if (actor.EntraTenantId is not null || actor.EntraObjectId is not null)
                throw new InvalidOperationException("The Playground sandbox actor must not carry directory identity values.");
            return actor;
        }

        var key = GalleryIdentity.Key(request);
        if (key is null) return null;
        var (tenantId, objectId) = key.Value;
        var displayName = GalleryIdentity.DisplayName(request);
        if (displayName.Length > 200) displayName = displayName[..200];

        var existing = await dbContext.Users.SingleOrDefaultAsync(
            u => u.EntraTenantId == tenantId && u.EntraObjectId == objectId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
            {
                existing.DisplayName = displayName;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return existing;
        }

        var user = new User
        {
            Id = Guid.NewGuid(), EntraTenantId = tenantId, EntraObjectId = objectId, DisplayName = displayName,
        };
        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Concurrent first sign-ins converge on the database's unique (tid, oid) key.
            dbContext.Entry(user).State = EntityState.Detached;
            return await dbContext.Users.SingleAsync(
                u => u.EntraTenantId == tenantId && u.EntraObjectId == objectId, cancellationToken);
        }

    }

    public static readonly Guid SandboxActorId = Guid.Parse("a4c02c98-a4d3-4f4f-9f93-bdb6b8ebd9b8");
}
