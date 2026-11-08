using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class GetSessionFunctionTests
{
    [Fact]
    public async Task Session_UsesOnlyTheValidatedPrincipal()
    {
        var request = new DefaultHttpContext().Request;
        MethodTestSupport.Principal(request, MethodTestSupport.User("Gallery person"));
        var result = Assert.IsType<OkObjectResult>(await new GetSession().Run(request));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.True(document.RootElement.GetProperty("isAuthorized").GetBoolean());
        Assert.Equal("Gallery person", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Session_RejectsForgedPlatformHeader()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["x-ms-client-principal"] = "eyJ1c2VySWQiOiJhZG1pbiJ9";
        Assert.IsType<UnauthorizedResult>(await new GetSession().Run(request));
    }

    [Fact]
    public async Task CurrentUser_DoesNotInventAnonymousIdentity()
    {
        await using var database = await RelationalTestDb.Create();
        await using var db = database.Context();
        Assert.Null(await new CurrentUserService(db).GetOrCreateCurrentUserAsync(new DefaultHttpContext().Request));
        Assert.Empty(db.Users);
    }

    [Fact]
    public async Task CurrentUser_SeparatesDirectories_AndDoesNotLinkLegacyRows()
    {
        await using var database = await RelationalTestDb.Create();
        await using var db = database.Context();
        var legacy = MethodTestSupport.User("Same display name");
        legacy.EntraTenantId = null;
        db.Users.Add(legacy);
        await db.SaveChangesAsync();

        var user = MethodTestSupport.User(legacy.DisplayName);
        user.EntraObjectId = legacy.EntraObjectId;
        var request = MethodTestSupport.Principal(new DefaultHttpContext().Request, user);
        var service = new CurrentUserService(db);
        var first = await service.GetOrCreateCurrentUserAsync(request);
        Assert.NotEqual(legacy.Id, first!.Id);

        user.EntraTenantId = Guid.NewGuid().ToString();
        MethodTestSupport.Principal(request, user);
        var second = await service.GetOrCreateCurrentUserAsync(request);
        Assert.NotEqual(first.Id, second!.Id);
        Assert.Equal(3, db.Users.Count());
    }
}
