using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Data;

namespace WorkflowGallery.Api.Tests.TestSupport;

/// <summary>
/// Creates an isolated EF Core InMemory <see cref="AppDbContext"/> per test (unique database name),
/// so tests never share state (T052 smoke coverage).
/// </summary>
public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
