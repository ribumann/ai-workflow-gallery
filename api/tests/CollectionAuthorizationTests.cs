using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public sealed class CollectionAuthorizationTests
{
    private const string Group = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    private static IConfiguration Configuration(params (string Key, string? Value)[] changes)
    {
        var values = new Dictionary<string, string?>
        {
            ["Entra:ApiClientId"] = "22222222-2222-4222-8222-222222222222",
            ["Entra:RequiredScope"] = "Gallery.Access",
            ["Entra:AllowedTenantIds"] = TenantId,
            ["Entra:McpResource"] = "https://gallery.example/api/mcp",
            ["CollectionAuthorization:Enabled"] = "true",
            ["CollectionAuthorization:TenantId"] = TenantId,
            ["CollectionAuthorization:Collections:public-techniques:ReadRoles"] = "Gallery.Reader",
            ["CollectionAuthorization:Collections:curated-tools:WriteRoles"] = "Gallery.Publisher",
            ["CollectionAuthorization:Collections:curated-tools:ReadGroups"] = Group,
        };
        foreach (var (key, value) in changes) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpRequest Request(string collection = CollectionScope.CuratedTools, string? role = null,
        string? group = null, string? tenant = TenantId, bool authenticated = true)
    {
        var request = Query(("collectionId", collection));
        var claims = new List<Claim> { new("oid", "33333333-3333-4333-8333-333333333333") };
        if (tenant is not null) claims.Add(new("tid", tenant));
        if (role is not null) claims.Add(new("roles", role));
        if (group is not null) claims.Add(new("groups", group));
        request.HttpContext.User = new(new ClaimsIdentity(claims, authenticated ? GalleryIdentity.AuthenticationType : null));
        request.HttpContext.Response.Body = new MemoryStream();
        return request;
    }

    [Theory]
    [InlineData("Gallery.Publisher", null, "curated-tools", true, true)]
    [InlineData("Gallery.Reader", null, "public-techniques", true, false)]
    [InlineData(null, Group, "curated-tools", true, false)]
    [InlineData(null, "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA", "curated-tools", true, false)]
    [InlineData(null, null, "curated-tools", false, false)]
    [InlineData("Gallery.Admin", null, "curated-tools", false, false)]
    [InlineData("admin", null, "curated-tools", false, false)]
    [InlineData("Gallery.Unmapped", null, "curated-tools", false, false)]
    [InlineData("gallery.publisher", null, "curated-tools", false, false)]
    [InlineData(null, "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", "curated-tools", false, false)]
    [InlineData("Gallery.Publisher", null, "public-techniques", false, false)]
    [InlineData("Gallery.Reader", null, "curated-tools", false, false)]
    [InlineData("Gallery.Publisher", null, "playground", false, false)]
    [InlineData("Gallery.Publisher", null, "unknown", false, false)]
    public void ExactGrants_DoNotCrossCollections(string? role, string? group, string collection, bool read, bool write)
    {
        var policy = CollectionAuthorizationPolicy.Read(Configuration());
        var request = Request(collection, role, group);
        Assert.Equal(read, policy.Allows(request, collection));
        Assert.Equal(write, policy.Allows(request, collection, write: true));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("44444444-4444-4444-8444-444444444444", true)]
    [InlineData(TenantId, false)]
    public void MissingForeignOrUnauthenticatedIdentity_IsDenied(string? tenant, bool authenticated)
    {
        var policy = CollectionAuthorizationPolicy.Read(Configuration());
        Assert.False(policy.HasAccess(Request(role: "Gallery.Publisher", tenant: tenant, authenticated: authenticated)));
    }

    [Fact]
    public void WriteGroup_IncludesReadWithoutGrantingOtherCollections()
    {
        var policy = CollectionAuthorizationPolicy.Read(Configuration(
            ("CollectionAuthorization:Collections:curated-tools:WriteGroups", Group)));
        var request = Request(group: Group);
        Assert.True(policy.Allows(request, "curated-tools"));
        Assert.True(policy.Allows(request, "curated-tools", write: true));
        Assert.False(policy.Allows(request, "public-techniques", write: true));
    }

    [Fact]
    public void OnlyClaimsOnValidatedIdentityCount_NoDisplayNameOrOverageLookup()
    {
        var policy = CollectionAuthorizationPolicy.Read(Configuration());
        var request = Request();
        request.HttpContext.User.AddIdentity(new ClaimsIdentity([
            new Claim("roles", "Gallery.Publisher"), new Claim("groups", Group),
        ], "Untrusted"));
        var identity = Assert.IsType<ClaimsIdentity>(request.HttpContext.User.Identity);
        identity.AddClaim(new("name", "Gallery.Publisher"));
        identity.AddClaim(new("hasgroups", "true"));
        identity.AddClaim(new("_claim_names", """{"groups":"external"}"""));
        Assert.False(policy.HasAccess(request));
    }

    [Theory]
    [InlineData("CollectionAuthorization:Enabled", "invalid")]
    [InlineData("CollectionAuthorization:Enabled:unexpected", "true")]
    [InlineData("CollectionAuthorization:Enabled", "false")]
    [InlineData("CollectionAuthorization:Enabled", null)]
    [InlineData("CollectionAuthorization:TenantId", "44444444-4444-4444-8444-444444444444")]
    [InlineData("CollectionAuthorization:TenantId", "")]
    [InlineData("Entra:AllowedTenantIds", TenantId + ",44444444-4444-4444-8444-444444444444")]
    [InlineData("CollectionAuthorization:Collections:unknown:ReadRoles", "Gallery.Reader")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:WriteRole", "Gallery.Publisher")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:WriteRoles", "")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:ReadGroups", "not-a-guid")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:ReadGroups", "00000000-0000-0000-0000-000000000000")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:WriteRoles", "Gallery.Publisher,")]
    [InlineData("CollectionAuthorization:Collections:curated-tools:WriteRoles:0", "Gallery.Publisher")]
    [InlineData("PublicDemoEnabled", "true")]
    public void InvalidOrConflictingConfiguration_FailsClosed(string key, string? value) =>
        Assert.Throws<InvalidOperationException>(() => CollectionAuthorizationPolicy.Read(Configuration((key, value))));

    [Fact]
    public void AbsentPolicy_PreservesExistingPrivateProfile()
    {
        var policy = CollectionAuthorizationPolicy.Read(new ConfigurationBuilder().Build());
        Assert.False(policy.Enabled);
        Assert.True(policy.HasAccess(Request()));
    }

    [Theory]
    [InlineData(null, TenantId, false)]
    [InlineData("Gallery.Unmapped", TenantId, false)]
    [InlineData("Gallery.Publisher", "44444444-4444-4444-8444-444444444444", false)]
    [InlineData("Gallery.Publisher", TenantId, true)]
    public async Task HttpBoundary_DeniesUnmappedOrForeignValidatedPrincipal(string? role, string tenant, bool allowed)
    {
        var request = Request(role: role, tenant: tenant);
        var boundary = new GalleryRequestAuthorization(Configuration(), new Validator(request.HttpContext.User),
            NullLogger<GalleryRequestAuthorization>.Instance);
        Assert.Equal(allowed, await boundary.AuthorizeAsync(request.HttpContext, Ct));
        Assert.Equal(allowed ? 200 : 403, request.HttpContext.Response.StatusCode);
        Assert.True(CollectionAuthorizationPolicy.For(request).Enabled);
    }

    [Fact]
    public async Task RestAndActualMcpHandlers_EnforceReadWriteAndCollectionScope()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        db.Users.Add(user);
        db.AiMethods.Add(Method(user, "Other organization collection"));
        await db.SaveChangesAsync();
        var policy = CollectionAuthorizationPolicy.Read(Configuration());
        var methods = new Methods(db, new FakeCurrentUserService(user));

        var reader = Request(group: Group);
        policy.Stamp(reader.HttpContext);
        Assert.IsType<OkObjectResult>(await methods.List(reader, Ct));
        var deniedWrite = MethodBody("Denied write");
        deniedWrite.Method = "POST";
        deniedWrite.QueryString = reader.QueryString;
        deniedWrite.HttpContext.User = reader.HttpContext.User;
        policy.Stamp(deniedWrite.HttpContext);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await methods.Create(deniedWrite, Ct)).StatusCode);

        var publisher = Request(role: "Gallery.Publisher");
        policy.Stamp(publisher.HttpContext);
        var allowedWrite = MethodBody("REST team method");
        allowedWrite.Method = "POST";
        allowedWrite.QueryString = publisher.QueryString;
        allowedWrite.HttpContext.User = publisher.HttpContext.User;
        policy.Stamp(allowedWrite.HttpContext);
        Assert.IsType<CreatedResult>(await methods.Create(allowedWrite, Ct));
        publisher.QueryString = new("?collectionId=public-techniques");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await methods.List(publisher, Ct)).StatusCode);

        var host = McpTestHost.Create(db, user, writesEnabled: true);
        async Task<JsonElement> Call(string name, string collection, object args)
        {
            var arguments = JsonSerializer.SerializeToNode(args)!.AsObject();
            arguments["collectionId"] = collection;
            var request = McpTestHost.Request(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments },
            }));
            request.HttpContext.User = publisher.HttpContext.User;
            policy.Stamp(request.HttpContext);
            var response = McpTestJson.ReadBody(await host.Endpoint.Post(request, Ct));
            Assert.Null(response!["error"]);
            return JsonSerializer.SerializeToElement(response["result"]);
        }
        var allowed = await Call("create_method", "curated-tools",
            new { title = "Published team method", summary = "Team summary", approach = "Team approach" });
        Assert.False(allowed.TryGetProperty("isError", out var error) && error.GetBoolean());
        var denied = await Call("search_methods", "public-techniques", new { });
        Assert.True(denied.GetProperty("isError").GetBoolean());
        db.CollectionId = CollectionScope.CuratedTools;
        Assert.Equal(2, db.AiMethods.Count());
        Assert.Equal(3, db.AiMethods.IgnoreQueryFilters().Count());
    }

    [Fact]
    public async Task Session_ReportsReadOnlyGrant_AndDeniesAnUnmappedCollection()
    {
        var request = Request(group: Group);
        CollectionAuthorizationPolicy.Read(Configuration()).Stamp(request.HttpContext);
        var session = Assert.IsType<OkObjectResult>(await new GetSession().Run(request));
        var body = JsonSerializer.SerializeToElement(session.Value);
        Assert.False(body.GetProperty("collectionCapabilities").GetProperty("canWrite").GetBoolean());
        request.QueryString = new("?collectionId=public-techniques");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await new GetSession().Run(request)).StatusCode);
        request.QueryString = QueryString.Empty;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await new GetSession().Run(request)).StatusCode);
    }

    private sealed class Validator(ClaimsPrincipal principal) : IEntraTokenValidator
    {
        public Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new GalleryAuthResult(GalleryAuthStatus.Success, principal));
    }
}
