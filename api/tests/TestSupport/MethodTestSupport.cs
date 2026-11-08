using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;

namespace WorkflowGallery.Api.Tests.TestSupport;

internal static class MethodTestSupport
{
    internal static readonly CancellationToken Ct = CancellationToken.None;
    internal const string TenantId = "11111111-1111-4111-8111-111111111111";

    internal static User User(string name = "Contributor") => new()
    {
        Id = Guid.NewGuid(), EntraTenantId = TenantId, EntraObjectId = Guid.NewGuid().ToString(), DisplayName = name,
    };

    internal static AiMethod Method(User user, string title = "Synthesis") => new()
    {
        Id = Guid.NewGuid(), Title = title, Summary = "Combine relevant sources", Approach = "# Approach\n\nCompare evidence.",
        CreatedByUser = user, CreatedByUserId = user.Id, CreatedAt = DateTime.UtcNow.AddDays(-1),
        LastEditedByUser = user, LastEditedByUserId = user.Id, LastEditedAt = DateTime.UtcNow.AddDays(-1),
        Version = Guid.NewGuid(),
    };

    internal static HttpRequest MethodBody(string title = "Synthesis", string summary = "Combine relevant sources",
        string approach = "# Approach\n\nCompare evidence.", Guid? version = null) =>
        TestHttpRequestFactory.CreateWithJsonBody(JsonSerializer.Serialize(new { title, summary, approach, expectedVersion = version }));

    internal static HttpRequest Query(params (string Key, string Value)[] values) =>
        TestHttpRequestFactory.CreateWithQuery(values.ToDictionary(v => v.Key, v => new StringValues(v.Value)));

    internal static HttpRequest Principal(HttpRequest request, User user, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("tid", user.EntraTenantId ?? TenantId),
            new("oid", user.EntraObjectId), new("name", user.DisplayName),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        request.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            claims, Services.Auth.GalleryIdentity.AuthenticationType, "name", "roles"));
        return request;
    }

    internal sealed class InterleavingUserService(User? user, Func<Task> beforeReturn) : ICurrentUserService
    {
        public async Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            await beforeReturn();
            return user;
        }
    }

    internal sealed class SaveHook : SaveChangesInterceptor
    {
        internal Func<DbContext, Task>? BeforeSave { get; set; }
        internal Func<DbContext, Task>? AfterSave { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null) await BeforeSave(eventData.Context!);
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (AfterSave is not null) await AfterSave(eventData.Context!);
            return result;
        }
    }

    // SqlClient exposes no public error factory. Construct real provider errors only in tests,
    // to exercise SQL-specific classification without connecting to a deployed database.
    internal static DbUpdateException SqlFailure(string constraint, int number = 547)
    {
        var errorConstructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 8);
        var error = (SqlError)errorConstructor.Invoke(
            [number, (byte)1, (byte)16, "test", $"Conflict with constraint \"{constraint}\".", "test", 1, null]);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, [error]);
        var factory = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static,
            binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!;
        return new DbUpdateException("Provider rejected the write.", (SqlException)factory.Invoke(null, [errors, "16.0"])!);
    }
}

internal sealed class RelationalTestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    internal DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    internal static async Task<RelationalTestDb> Create(params IInterceptor[] interceptors)
    {
        var database = new RelationalTestDb();
        await database._connection.OpenAsync();
        database.Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(database._connection).AddInterceptors(interceptors).Options;
        await using var db = database.Context();
        await db.Database.EnsureCreatedAsync();
        return database;
    }

    internal AppDbContext Context() => new(Options);
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
