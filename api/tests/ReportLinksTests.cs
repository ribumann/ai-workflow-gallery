using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Primitives;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class ReportLinksTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static User NewUser(string name = "Contributor") =>
        new() { Id = Guid.NewGuid(), EntraObjectId = Guid.NewGuid().ToString(), DisplayName = name };
    private static HttpRequest Body(string name = "Quarterly outcomes", string description = "Review delivery outcomes.",
        string url = "https://reports.example.org/embed?id=123#page", string kind = "Report", Guid? version = null) =>
        TestHttpRequestFactory.CreateWithJsonBody(JsonSerializer.Serialize(new
        {
            name, description, url, kind, expectedVersion = version,
        }));
    private static HttpRequest Query(params (string Key, string Value)[] values) =>
        TestHttpRequestFactory.CreateWithQuery(values.ToDictionary(v => v.Key, v => new StringValues(v.Value)));
    private static ReportLink NewLink(User user, string name = "Report", ReportLinkKind kind = ReportLinkKind.Report) => new()
    {
        Id = Guid.NewGuid(), Name = name, Description = "Business outcomes", Url = "https://reports.example.org/",
        Kind = kind, CreatedByUser = user, CreatedByUserId = user.Id,
        LastEditedByUser = user, LastEditedByUserId = user.Id,
        CreatedAt = DateTime.UtcNow, LastEditedAt = DateTime.UtcNow, Version = Guid.NewGuid(),
    };

    [Fact]
    public async Task SharedCrud_PreservesCreator_AndChangesEditorAndVersion()
    {
        await using var db = TestDbContextFactory.Create();
        var creator = NewUser("Creator");
        var editor = NewUser("Another user");
        db.Users.AddRange(creator, editor);
        await db.SaveChangesAsync();
        var create = new Reports(db, new FakeCurrentUserService(creator));
        var response = Assert.IsType<ObjectResult>(await create.Create(Body(name: "  Team report  "), Ct));
        Assert.Equal(201, response.StatusCode);
        var created = Assert.IsType<ReportLinkDto>(response.Value);
        Assert.Equal("Team report", created.Name);
        Assert.Equal("Creator", created.CreatedByDisplayName);
        Assert.NotEqual(Guid.Empty, created.Version);

        var edit = new Reports(db, new FakeCurrentUserService(editor));
        var updated = Assert.IsType<ReportLinkDto>(Assert.IsType<OkObjectResult>(
            await edit.Update(Body(name: "New name", kind: "AiApp", version: created.Version), created.Id, Ct)).Value);
        Assert.Equal("Creator", updated.CreatedByDisplayName);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal("Another user", updated.LastEditedByDisplayName);
        Assert.NotEqual(created.Version, updated.Version);
        Assert.Equal(ReportLinkKind.AiApp, updated.Kind);
        Assert.Equal("New name", updated.Name);

        var detail = Assert.IsType<ReportLinkDto>(Assert.IsType<OkObjectResult>(await edit.GetById(Query(), created.Id, Ct)).Value);
        Assert.Equal(updated, detail);
        Assert.IsType<NoContentResult>(await edit.Delete(Query(), created.Id, Ct));
        Assert.Empty(db.ReportLinks);
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Fact]
    public async Task List_FiltersSearchesAndPages_WithStableNameOrdering()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = NewUser();
        db.Users.Add(owner);
        db.ReportLinks.AddRange(NewLink(owner, "Zulu"), NewLink(owner, "Alpha"), NewLink(owner, "App", ReportLinkKind.AiApp));
        await db.SaveChangesAsync();
        var function = new Reports(db, new FakeCurrentUserService(owner));
        var page = Assert.IsType<PagedResultDto<ReportLinkDto>>(Assert.IsType<OkObjectResult>(
            await function.List(Query(("kind", "Report"), ("q", "OUTCOMES"), ("page", "2"), ("pageSize", "1")), Ct)).Value);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal("Zulu", Assert.Single(page.Items).Name);
        var empty = Assert.IsType<PagedResultDto<ReportLinkDto>>(Assert.IsType<OkObjectResult>(
            await function.List(Query(("q", "absent")), Ct)).Value);
        Assert.Empty(empty.Items);
        Assert.Equal(25, empty.PageSize);
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "2147483647")]
    [InlineData("page", "999999999999999")]
    [InlineData("page", "1.1")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "")]
    [InlineData("kind", "0")]
    [InlineData("kind", "99")]
    [InlineData("kind", "Unknown")]
    public async Task List_RejectsInvalidQuery(string key, string value)
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Reports(db, new FakeCurrentUserService(null));
        Assert.IsType<BadRequestObjectResult>(await function.List(Query((key, value)), Ct));
    }

    [Fact]
    public async Task List_RejectsOversizedSearch()
    {
        await using var db = TestDbContextFactory.Create();
        Assert.IsType<BadRequestObjectResult>(await new Reports(db, new FakeCurrentUserService(null))
            .List(Query(("q", new string('x', 201))), Ct));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.org/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//example.org/")]
    [InlineData("https:///example.org")]
    [InlineData("https://user:pass@example.org/")]
    [InlineData("https://example.org\\@evil.example/")]
    [InlineData("https://example.org/\npath")]
    [InlineData("https://example.org/\tpath")]
    [InlineData("https://example.org/with space")]
    [InlineData("https://example.org/\u007f")]
    [InlineData("https://example.org/\u0085")]
    [InlineData("https://example.org/\u00a0")]
    [InlineData("https://example.org/\u2003")]
    [InlineData("https://")]
    public async Task Create_RejectsUnsafeDestinations(string url)
    {
        await using var db = TestDbContextFactory.Create();
        Assert.IsType<BadRequestObjectResult>(await new Reports(db, new FakeCurrentUserService(NewUser())).Create(Body(url: url), Ct));
        Assert.Empty(db.ReportLinks);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\":\"a\",\"description\":\"d\",\"url\":\"https://example.org\",\"kind\":0}")]
    [InlineData("{\"name\":\"a\",\"description\":\"d\",\"url\":\"https://example.org\",\"kind\":\"Unknown\"}")]
    public async Task Create_RejectsMalformedOrIncompleteBody(string json)
    {
        await using var db = TestDbContextFactory.Create();
        Assert.IsType<BadRequestObjectResult>(await new Reports(db, new FakeCurrentUserService(NewUser()))
            .Create(TestHttpRequestFactory.CreateWithJsonBody(json), Ct));
    }

    [Fact]
    public async Task Create_EnforcesFieldLimits_AndAcceptsBoundaryValues()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var function = new Reports(db, new FakeCurrentUserService(user));
        Assert.IsType<BadRequestObjectResult>(await function.Create(Body(name: " "), Ct));
        Assert.IsType<BadRequestObjectResult>(await function.Create(Body(description: " "), Ct));
        Assert.IsType<BadRequestObjectResult>(await function.Create(Body(name: new string('a', 201)), Ct));
        Assert.IsType<BadRequestObjectResult>(await function.Create(Body(description: new string('a', 4001)), Ct));
        Assert.IsType<BadRequestObjectResult>(await function.Create(Body(url: "https://example.org/" + new string('a', 2048)), Ct));
        var result = Assert.IsType<ObjectResult>(await function.Create(Body(
            name: new string('a', 200), description: new string('b', 4000),
            url: "https://example.org/" + new string('c', 2048 - "https://example.org/".Length)), Ct));
        Assert.Equal(201, result.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsMissingEmptyOrStaleVersions_WithoutOverwriting()
    {
        await using var db = TestDbContextFactory.Create();
        var user = NewUser();
        var link = NewLink(user);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();
        var function = new Reports(db, new FakeCurrentUserService(user));
        Assert.IsType<BadRequestObjectResult>(await function.Update(Body(), link.Id, Ct));
        Assert.IsType<BadRequestObjectResult>(await function.Update(Body(version: Guid.Empty), link.Id, Ct));
        Assert.IsType<ConflictObjectResult>(await function.Update(Body(version: Guid.NewGuid()), link.Id, Ct));
        Assert.Equal("Report", link.Name);
        Assert.IsType<BadRequestObjectResult>(await function.Update(Body(url: "javascript:bad", version: link.Version), link.Id, Ct));
    }

    [Fact]
    public async Task MissingLinks_ReturnNotFound()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Reports(db, new FakeCurrentUserService(NewUser()));
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await function.GetById(Query(), id, Ct));
        Assert.IsType<NotFoundResult>(await function.Update(Body(version: Guid.NewGuid()), id, Ct));
        Assert.IsType<NotFoundResult>(await function.Delete(Query(), id, Ct));
    }

    [Fact]
    public async Task Writes_RejectMissingCurrentUser()
    {
        await using var db = TestDbContextFactory.Create();
        var link = NewLink(NewUser());
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();
        var function = new Reports(db, new FakeCurrentUserService(null));
        Assert.IsType<UnauthorizedResult>(await function.Create(Body(), Ct));
        Assert.IsType<UnauthorizedResult>(await function.Update(Body(version: link.Version), link.Id, Ct));
        Assert.IsType<UnauthorizedResult>(await function.Delete(Query(), link.Id, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_DetectsACompetingDatabaseWrite_AfterVersionPrecheck(bool delete)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var owner = NewUser();
        var link = NewLink(owner);
        db.ReportLinks.Add(link);
        await db.SaveChangesAsync();
        var originalVersion = link.Version;
        var userService = new InterleavingUserService(owner, async () =>
        {
            await using var other = new AppDbContext(options);
            var competing = await other.ReportLinks.SingleAsync();
            if (delete) other.ReportLinks.Remove(competing);
            else { competing.Name = "Competing edit"; competing.Version = Guid.NewGuid(); }
            await other.SaveChangesAsync();
        });
        var response = await new Reports(db, userService).Update(Body(version: originalVersion), link.Id, Ct);
        if (delete) Assert.IsType<NotFoundResult>(response);
        else
        {
            Assert.IsType<ConflictObjectResult>(response);
            await using var verify = new AppDbContext(options);
            Assert.Equal("Competing edit", (await verify.ReportLinks.SingleAsync()).Name);
        }
    }

    [Fact]
    public void FreshSchema_IncludesReportLinks_AndModelUsesConcurrency()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        var entity = db.Model.FindEntityType(typeof(ReportLink))!;
        Assert.True(entity.FindProperty(nameof(ReportLink.Version))!.IsConcurrencyToken);
        foreach (var propertyName in new[] { nameof(ReportLink.CreatedAt), nameof(ReportLink.LastEditedAt) })
        {
            var converter = entity.FindProperty(propertyName)!.GetValueConverter()!;
            var value = Assert.IsType<DateTime>(converter.ConvertFromProvider(new DateTime(2026, 9, 10)));
            Assert.Equal(DateTimeKind.Utc, value.Kind);
            Assert.Contains("Z", JsonSerializer.Serialize(value));
        }
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Empty(db.GetService<IMigrationsAssembly>().Migrations);
        var script = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE [ReportLinks]", script);
        Assert.Contains("[Url] nvarchar(2048)", script);
        Assert.Contains("CREATE TABLE [CatalogSchemaMarker]", script);
        Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class InterleavingUserService(User user, Func<Task> beforeReturn) : ICurrentUserService
    {
        public async Task<User?> GetOrCreateCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            await beforeReturn();
            return user;
        }
    }
}
