using Microsoft.AspNetCore.Http;
using WorkflowGallery.Api.Services.Mcp;
using WorkflowGallery.Api.Tests.TestSupport;
using Xunit;

namespace WorkflowGallery.Api.Tests;

/// <summary>
/// Resolution of the `galleryUrl` back-link's origin. Two of the three fallbacks read headers the
/// caller may be able to set, and the result is handed to an agent as the citable link for a real
/// gallery record — so an unvalidated origin would attach an attacker-chosen destination to
/// authentic content.
/// </summary>
public class McpGalleryLinkTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static string? Build(string? configured, params (string Name, string Value)[] headers)
    {
        var request = McpTestHost.Request("");
        request.QueryString = new("?collectionId=public-techniques");
        request.Host = new HostString("func-gallery-uat-example.azurewebsites.net");
        request.Scheme = "https";

        foreach (var (name, value) in headers)
        {
            request.Headers[name] = value;
        }

        return new GalleryLinkBuilder(configured).BuildItemUrl(request, GalleryLinkBuilder.TaskPath, Id);
    }

    [Fact]
    public void TheConfiguredBaseUrl_WinsOverAnythingTheCallerSends()
    {
        var url = Build("https://gallery.example.com/", ("x-forwarded-host", "evil.example.net"));

        Assert.Equal($"https://gallery.example.com/tasks/{Id}?collectionId=public-techniques", url);
    }

    [Fact]
    public void TheProxysOriginalUrl_IsUsedWhenNoBaseUrlIsConfigured()
    {
        var url = Build(null, ("x-ms-original-url", "https://icy-stone.azurestaticapps.net/api/mcp"));

        Assert.Equal($"https://icy-stone.azurestaticapps.net/tasks/{Id}?collectionId=public-techniques", url);
    }

    [Fact]
    public void TheForwardedHost_IsUsedWhenThereIsNoOriginalUrl()
    {
        var url = Build(null, ("x-forwarded-host", "icy-stone.azurestaticapps.net, inner.local"));

        Assert.Equal($"https://icy-stone.azurestaticapps.net/tasks/{Id}?collectionId=public-techniques", url);
    }

    [Theory]
    [InlineData("javascript:alert(1)//")]
    [InlineData("evil.example.net/phishing")]
    [InlineData("gallery.example.com@evil.example.net")]
    [InlineData("evil.example.net?next=x")]
    [InlineData("evil.example.net#x")]
    public void AnUnusableForwardedHost_FallsBackToTheRequestsOwnOrigin(string forwardedHost)
    {
        var url = Build(null, ("x-forwarded-host", forwardedHost));

        Assert.Equal($"https://func-gallery-uat-example.azurewebsites.net/tasks/{Id}?collectionId=public-techniques", url);
    }

    [Fact]
    public void ANonHttpForwardedProto_IsRefused()
    {
        var url = Build(
            null,
            ("x-forwarded-host", "gallery.example.com"),
            ("x-forwarded-proto", "javascript"));

        Assert.Equal($"https://func-gallery-uat-example.azurewebsites.net/tasks/{Id}?collectionId=public-techniques", url);
    }
}
