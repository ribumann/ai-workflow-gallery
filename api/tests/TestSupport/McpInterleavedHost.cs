using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Mcp;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>
/// Builds the MCP tool layer over endpoints that run a hook the moment a detail read completes.
///
/// <para>
/// Every update tool reads the item it is about to change and builds the request body from what it
/// read. That is what makes a partial update possible, and it is also the one window in which
/// someone else's edit can be lost: read at T1, write the pre-read values back at T2. Whether a
/// tool guards that window is not observable from outside — both the guarded and unguarded versions
/// succeed when nothing else is happening. This harness opens the window deliberately so a test can
/// commit a competing edit inside it and assert what the tool does about it.
/// </para>
/// </summary>
internal static class McpInterleavedHost
{
    internal static IGalleryMcpTools Create(AppDbContext db, User user, Func<Task> afterRead)
    {
        ICurrentUserService currentUser = new FakeCurrentUserService(user);

        return new GalleryMcpTools(
            new Tasks(db, currentUser, new FakeBlobStorageService(), new FakeFileValidationService()),
            new InterleavingWorkflows(db, currentUser, afterRead),
            new InterleavingMethods(db, currentUser, afterRead),
            new InterleavingReports(db, currentUser, afterRead),
            new GalleryLinkBuilder(McpTestHost.BaseUrl));
    }

    private sealed class InterleavingWorkflows(AppDbContext db, ICurrentUserService currentUser, Func<Task> afterRead)
        : Workflows(db, currentUser)
    {
        public override async Task<IActionResult> GetById(HttpRequest req, Guid id, CancellationToken cancellationToken)
        {
            var result = await base.GetById(req, id, cancellationToken);
            await afterRead();
            return result;
        }
    }

    private sealed class InterleavingMethods(AppDbContext db, ICurrentUserService currentUser, Func<Task> afterRead)
        : Methods(db, currentUser)
    {
        public override async Task<IActionResult> GetById(HttpRequest req, Guid id, CancellationToken cancellationToken)
        {
            var result = await base.GetById(req, id, cancellationToken);
            await afterRead();
            return result;
        }
    }

    private sealed class InterleavingReports(AppDbContext db, ICurrentUserService currentUser, Func<Task> afterRead)
        : Reports(db, currentUser)
    {
        public override async Task<IActionResult> GetById(HttpRequest req, Guid id, CancellationToken cancellationToken)
        {
            var result = await base.GetById(req, id, cancellationToken);
            await afterRead();
            return result;
        }
    }
}
