using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using WorkflowGallery.Api.Data;
using WorkflowGallery.Api.Dtos;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Models;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;
using static WorkflowGallery.Api.Tests.TestSupport.MethodTestSupport;

namespace WorkflowGallery.Api.Tests;

public class MethodsFunctionTests
{
    [Fact]
    public async Task Crud_TrimsContent_PreservesAuthor_ChangesEditorAndVersion()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var creator = User("Creator");
        var editor = User("Editor");
        db.Users.AddRange(creator, editor);
        await db.SaveChangesAsync();
        var function = new Methods(db, new FakeCurrentUserService(creator));
        var response = Assert.IsType<CreatedResult>(await function.Create(
            MethodBody(" Title ", " Summary ", " \n# Approach\n\n**Evidence**\n "), Ct));
        var created = Assert.IsType<MethodDetailDto>(response.Value);
        Assert.Equal($"/api/methods/{created.Id}?collectionId=public-techniques", response.Location);
        Assert.Equal("Title", created.Title);
        Assert.Equal("Summary", created.Summary);
        Assert.Equal("# Approach\n\n**Evidence**", created.Approach);
        Assert.Equal(new UserRefDto(creator.Id, "Creator"), created.CreatedBy);
        Assert.Equal(created.CreatedBy, created.LastEditedBy);
        Assert.Equal(created.CreatedAt, created.LastEditedAt);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(Guid.Empty, created.Version);
        Assert.True(created.CanDelete);

        var updated = Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(
            await new Methods(db, new FakeCurrentUserService(editor)).Update(
                MethodBody("Revised", version: created.Version), created.Id, Ct)).Value);
        Assert.Equal(created.CreatedBy, updated.CreatedBy);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(editor.Id, updated.LastEditedBy.Id);
        Assert.True(updated.LastEditedAt >= created.LastEditedAt);
        Assert.NotEqual(created.Version, updated.Version);
        Assert.False(updated.CanDelete);
        var read = Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(
            await function.GetById(Principal(Query(), editor), created.Id, Ct)).Value);
        Assert.Equal(updated, read);

        Assert.IsType<NoContentResult>(await function.Delete(Query(), created.Id, Ct));
        Assert.IsType<NotFoundResult>(await function.GetById(Query(), created.Id, Ct));
        Assert.Equal(2, await db.Users.CountAsync());
    }

    [Fact]
    public async Task List_SearchesOnlyTitleAndSummary_InSql_WithStablePaging()
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var user = User();
        var alpha1 = Method(user, "Alpha");
        var alpha2 = Method(user, "Alpha");
        var zulu = Method(user, "Zulu");
        var bodyOnly = Method(user, "Other");
        bodyOnly.Summary = "Not a match";
        bodyOnly.Approach = "Combine relevant sources";
        db.AiMethods.AddRange(alpha2, zulu, alpha1, bodyOnly);
        await db.SaveChangesAsync();
        var function = new Methods(db, new FakeCurrentUserService(null));
        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = Assert.IsType<PagedResultDto<MethodSummaryDto>>(Assert.IsType<OkObjectResult>(
                await function.List(Query(("q", " RELEVANT "), ("page", page.ToString()), ("pageSize", "1")), Ct)).Value);
            Assert.Equal(3, result.TotalCount);
            Assert.Equal(page, result.Page);
            Assert.Equal(1, result.PageSize);
            var item = Assert.Single(result.Items);
            Assert.Equal(user.DisplayName, item.CreatedByDisplayName);
            ids.Add(item.Id);
        }
        Assert.Equal(new[] { alpha1.Id, alpha2.Id }.OrderBy(id => id).Append(zulu.Id), ids);
        var titleSearch = Assert.IsType<PagedResultDto<MethodSummaryDto>>(Assert.IsType<OkObjectResult>(
            await function.List(Query(("q", "ALPHA")), Ct)).Value);
        Assert.Equal(2, titleSearch.TotalCount);
        var empty = Assert.IsType<PagedResultDto<MethodSummaryDto>>(Assert.IsType<OkObjectResult>(
            await function.List(Query(("q", "absent")), Ct)).Value);
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.Page);
        Assert.Equal(25, empty.PageSize);
        Assert.IsType<OkObjectResult>(await function.List(Query(("page", int.MaxValue.ToString()), ("pageSize", "1")), Ct));
        Assert.IsType<OkObjectResult>(await function.List(Query(("pageSize", "100")), Ct));
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "2147483647")]
    [InlineData("page", "2147483648")]
    [InlineData("page", "1.1")]
    [InlineData("page", "x")]
    [InlineData("page", "")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "-1")]
    [InlineData("pageSize", "")]
    public async Task List_RejectsInvalidPaging(string key, string value)
    {
        await using var db = TestDbContextFactory.Create();
        AssertError(await new Methods(db, new FakeCurrentUserService(null)).List(Query((key, value)), Ct));
    }

    [Fact]
    public async Task List_RejectsOversizedSearch()
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Methods(db, new FakeCurrentUserService(null));
        AssertError(await function.List(Query(("q", new string('x', 201))), Ct));
        Assert.IsType<OkObjectResult>(await function.List(Query(("q", new string('x', 200))), Ct));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"title\":42,\"summary\":\"s\",\"approach\":\"a\"}")]
    [InlineData("{\"title\":\"t\",\"summary\":\"s\",\"approach\":\"a\",\"expectedVersion\":\"bad\"}")]
    public async Task Writes_RejectMalformedJson(string json)
    {
        await using var db = TestDbContextFactory.Create();
        var function = new Methods(db, new FakeCurrentUserService(User()));
        AssertError(await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), Ct));
        AssertError(await function.Update(TestHttpRequestFactory.CreateWithJsonBody(json), Guid.NewGuid(), Ct));
        Assert.Empty(db.AiMethods);
    }

    [Theory]
    [InlineData("title", 201)]
    [InlineData("summary", 2001)]
    [InlineData("approach", 100001)]
    [InlineData("title", 0)]
    [InlineData("summary", 0)]
    [InlineData("approach", 0)]
    public async Task Writes_EnforceRequiredFieldsAndLimits(string field, int length)
    {
        await using var db = TestDbContextFactory.Create();
        var user = User();
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var function = new Methods(db, new FakeCurrentUserService(user));
        var invalid = length == 0 ? " \n\t " : new string('x', length);
        var values = new Dictionary<string, object?>
        {
            ["title"] = "Title", ["summary"] = "Summary", ["approach"] = "Approach", ["expectedVersion"] = method.Version,
        };
        values[field] = invalid;
        var json = JsonSerializer.Serialize(values);
        AssertError(await function.Create(TestHttpRequestFactory.CreateWithJsonBody(json), Ct));
        AssertError(await function.Update(TestHttpRequestFactory.CreateWithJsonBody(json), method.Id, Ct));
        Assert.Equal("Synthesis", (await db.AiMethods.AsNoTracking().SingleAsync()).Title);
    }

    [Fact]
    public async Task Create_AcceptsBoundaryValues_AndIgnoresSpoofedMetadata()
    {
        await using var db = TestDbContextFactory.Create();
        var user = User();
        var function = new Methods(db, new FakeCurrentUserService(user));
        var created = Assert.IsType<MethodDetailDto>(Assert.IsType<CreatedResult>(await function.Create(
            MethodBody(new string('t', 200), new string('s', 2000), new string('a', 100000)), Ct)).Value);
        Assert.Equal(100000, created.Approach.Length);
        var spoofedId = Guid.NewGuid();
        var spoofed = TestHttpRequestFactory.CreateWithJsonBody(JsonSerializer.Serialize(new
        {
            title = "Title", summary = "Summary", approach = "Approach", id = spoofedId,
            createdByUserId = spoofedId, createdAt = DateTime.MinValue,
            lastEditedByUserId = spoofedId, lastEditedAt = DateTime.MinValue, version = spoofedId, canDelete = false,
        }));
        var dto = Assert.IsType<MethodDetailDto>(Assert.IsType<CreatedResult>(await function.Create(spoofed, Ct)).Value);
        Assert.NotEqual(spoofedId, dto.Id);
        Assert.NotEqual(spoofedId, dto.Version);
        Assert.Equal(user.Id, dto.CreatedBy.Id);
        Assert.Equal(user.Id, dto.LastEditedBy.Id);
        Assert.True(dto.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Update_RequiresVersion_AndRejectsStaleWithoutMutation()
    {
        await using var db = TestDbContextFactory.Create();
        var method = Method(User());
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var function = new Methods(db, new FakeCurrentUserService(method.CreatedByUser));
        AssertError(await function.Update(MethodBody(), method.Id, Ct));
        AssertError(await function.Update(MethodBody(version: Guid.Empty), method.Id, Ct));
        var result = Assert.IsType<ConflictObjectResult>(await function.Update(MethodBody("Overwrite", version: Guid.NewGuid()), method.Id, Ct));
        Assert.IsType<ErrorResponseDto>(result.Value);
        Assert.Equal("Synthesis", method.Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_DetectsDatabaseRaces_AfterPrecheck(bool delete)
    {
        await using var store = await RelationalTestDb.Create();
        await using var db = store.Context();
        var method = Method(User());
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var version = method.Version;
        var service = new InterleavingUserService(method.CreatedByUser, async () =>
        {
            await using var other = store.Context();
            var competing = await other.AiMethods.SingleAsync();
            if (delete) other.Remove(competing);
            else { competing.Title = "Competing edit"; competing.Version = Guid.NewGuid(); }
            await other.SaveChangesAsync();
        });
        var response = await new Methods(db, service).Update(MethodBody("Overwrite", version: version), method.Id, Ct);
        if (delete) Assert.IsType<NotFoundResult>(response);
        else
        {
            Assert.IsType<ErrorResponseDto>(Assert.IsType<ConflictObjectResult>(response).Value);
            Assert.Equal("Competing edit", (await db.AiMethods.AsNoTracking().SingleAsync()).Title);
        }
    }

    [Fact]
    public async Task MissingMethods_Return404_AndNullCurrentUserRejectsWrites()
    {
        await using var db = TestDbContextFactory.Create();
        var user = User();
        var function = new Methods(db, new FakeCurrentUserService(user));
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await function.GetById(Query(), id, Ct));
        Assert.IsType<NotFoundResult>(await function.Update(MethodBody(version: Guid.NewGuid()), id, Ct));
        Assert.IsType<NotFoundResult>(await function.Delete(Query(), id, Ct));
        var method = Method(user);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        function = new Methods(db, new FakeCurrentUserService(null));
        Assert.IsType<UnauthorizedResult>(await function.Create(MethodBody(), Ct));
        Assert.IsType<UnauthorizedResult>(await function.Update(MethodBody(version: method.Version), method.Id, Ct));
        Assert.IsType<UnauthorizedResult>(await function.Delete(Query(), method.Id, Ct));
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("contributor", false)]
    [InlineData("admin", true)]
    public async Task Delete_OnlyOwnerOrAdmin_RegardlessOfDisplayName(string role, bool allowed)
    {
        await using var db = TestDbContextFactory.Create();
        var owner = User("Same name");
        var other = User("Same name");
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        var viewer = role == "owner" ? owner : other;
        var function = new Methods(db, new FakeCurrentUserService(viewer));
        var detail = Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(
            await function.GetById(Principal(Query(), viewer, role), method.Id, Ct)).Value);
        Assert.Equal(allowed, detail.CanDelete);
        var result = await function.Delete(Principal(Query(), viewer, role), method.Id, Ct);
        if (allowed) Assert.IsType<NoContentResult>(result);
        else
        {
            Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
            Assert.Single(db.AiMethods);
        }
    }

    [Fact]
    public async Task ReadOwnership_UsesStoredIdentity_WithoutUserWrites()
    {
        await using var db = TestDbContextFactory.Create();
        var owner = User();
        var method = Method(owner);
        db.AiMethods.Add(method);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var function = new Methods(db, new InterleavingUserService(null, () => throw new InvalidOperationException("No read upsert")));
        var req = Principal(Query(), owner);
        var dto = Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(await function.GetById(req, method.Id, Ct)).Value);
        Assert.True(dto.CanDelete);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Single(db.Users);
        req.Headers["x-ms-client-principal"] = "malformed";
        Assert.True(Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(await function.GetById(req, method.Id, Ct)).Value).CanDelete);
        req.HttpContext.User = new System.Security.Claims.ClaimsPrincipal();
        Assert.False(Assert.IsType<MethodDetailDto>(Assert.IsType<OkObjectResult>(await function.GetById(req, method.Id, Ct)).Value).CanDelete);
    }

    [Fact]
    public void FreshSqlModel_PreservesRestrictiveAssociationsAndConcurrency()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        var model = db.Model.FindEntityType(typeof(AiMethod))!;
        Assert.True(model.FindProperty(nameof(AiMethod.Version))!.IsConcurrencyToken);
        foreach (var (name, length) in new[] { ("Title", 200), ("Summary", 2000), ("Approach", 100000) })
        {
            Assert.False(model.FindProperty(name)!.IsNullable);
            Assert.Equal(length, model.FindProperty(name)!.GetMaxLength());
        }
        foreach (var name in new[] { "CreatedAt", "LastEditedAt" })
            Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(model.FindProperty(name)!.GetValueConverter()!
                .ConvertFromProvider(new DateTime(2026, 9, 11))).Kind);
        Assert.All(model.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        foreach (var type in new[] { typeof(TaskItem), typeof(Workflow) })
        {
            var entity = db.Model.FindEntityType(type)!;
            var property = entity.FindProperty("MethodId")!;
            Assert.True(property.IsNullable);
            Assert.Contains(entity.GetIndexes(), i => i.Properties.SequenceEqual(new[] { property }));
            Assert.Equal(DeleteBehavior.Restrict, entity.GetForeignKeys().Single(f => f.PrincipalEntityType == model).DeleteBehavior);
        }
        Assert.Empty(db.GetService<IMigrationsAssembly>().Migrations);
        var script = db.Database.GenerateCreateScript();
        Assert.DoesNotContain("DROP ", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [WorkflowSteps]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE [AiMethods]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [Tasks]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE [Workflows]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FOREIGN KEY ([MethodId])", script);
        var sql = db.AiMethods.Where(m => m.Title.ToLower().Contains("needle") || m.Summary.ToLower().Contains("needle"))
            .OrderBy(m => m.Title).ThenBy(m => m.Id).Skip(25).Take(25).ToQueryString();
        Assert.Contains("WHERE", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("OFFSET", sql);
    }

    internal static void AssertError(IActionResult result) =>
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<ErrorResponseDto>(Assert.IsType<BadRequestObjectResult>(result).Value).Message));
}
