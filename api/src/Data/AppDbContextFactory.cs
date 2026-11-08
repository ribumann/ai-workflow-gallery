using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkflowGallery.Api.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add`/`database update` can construct an
/// <see cref="AppDbContext"/> without running the Functions host (which normally supplies the
/// connection string via app settings — see Program.cs). Never used at runtime.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Design-time-only placeholder connection string — schema generation does not need a
        // reachable server. Real connection strings come from app settings at runtime.
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=WorkflowGalleryDesignTime;Trusted_Connection=True;TrustServerCertificate=True;");

        return new AppDbContext(optionsBuilder.Options);
    }
}
