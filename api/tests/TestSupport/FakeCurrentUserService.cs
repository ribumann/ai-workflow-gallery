using Microsoft.AspNetCore.Http;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>Fake <see cref="ICurrentUserService"/>: returns a fixed user (or null to simulate an
/// unauthenticated request), bypassing real `x-ms-client-principal` header parsing in tests.</summary>
public class FakeCurrentUserService : ICurrentUserService
{
    private readonly User? _user;

    public FakeCurrentUserService(User? user)
    {
        _user = user;
    }

    public Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(_user);
}
