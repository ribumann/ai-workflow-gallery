using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WorkflowGallery.Api.Functions;
using WorkflowGallery.Api.Services.Auth;
using WorkflowGallery.Api.Services.Mcp;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public sealed class TenantAuthorizationMiddlewareTests : IDisposable
{
    private const string Tenant = "11111111-1111-4111-8111-111111111111";
    private const string Audience = "22222222-2222-4222-8222-222222222222";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly IConfigurationRoot _configuration;

    public TenantAuthorizationMiddlewareTests()
    {
        _key = new(_rsa) { KeyId = "local-test-key" };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entra:ApiClientId"] = Audience,
            ["Entra:RequiredScope"] = "Gallery.Access",
            ["Entra:AllowedTenantIds"] = Tenant,
            ["Entra:McpResource"] = "https://gallery.example/api/mcp",
            ["EnableMcpServer"] = "true",
        }).Build();
    }

    private string Token(string? tenant = Tenant, string? scope = "Gallery.Access", string? oid = null,
        string? issuer = null, string audience = Audience, DateTime? expires = null,
        string? idtyp = null, bool signed = true, string algorithm = SecurityAlgorithms.RsaSha256,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object> { ["ver"] = "2.0", ["oid"] = oid ?? Guid.NewGuid().ToString(), ["name"] = "Test person" };
        if (tenant is not null) claims["tid"] = tenant;
        if (scope is not null) claims["scp"] = scope;
        if (idtyp is not null) claims["idtyp"] = idtyp;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"https://login.microsoftonline.com/{tenant}/v2.0",
            Audience = audience, Claims = claims,
            NotBefore = DateTime.UtcNow.AddHours(-2), IssuedAt = DateTime.UtcNow.AddHours(-2),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = signed ? new SigningCredentials(signingKey ?? _key, algorithm) : null,
        });
    }

    private TestServer Server(bool unavailable = false)
    {
        var validator = new EntraTokenValidator(new Metadata(_key, unavailable));
        var authorization = new GalleryRequestAuthorization(_configuration, validator, NullLogger<GalleryRequestAuthorization>.Instance);
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services => services.AddControllers())
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == "/api/oauth-resource-metadata" ||
                        await authorization.AuthorizeAsync(context, context.RequestAborted))
                        await next();
                });
                app.Run(async context =>
                {
                    IActionResult result;
                    if (context.Request.Path == "/api/oauth-resource-metadata")
                        result = new OAuthResourceMetadata(_configuration, NullLogger<OAuthResourceMetadata>.Instance).Run(context.Request);
                    else if (context.Request.Path == "/api/mcp")
                    {
                        var options = new McpOptions { Enabled = true };
                        var endpoint = new Mcp(options, new McpToolCatalog(options), new NoTools(),
                            new Audit(), NullLogger<Mcp>.Instance);
                        result = await endpoint.Post(context.Request, context.RequestAborted);
                    }
                    else result = await new GetSession().Run(context.Request);
                    await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
                });
            }));
    }

    [Theory]
    [InlineData("/api/tasks")]
    [InlineData("/api/workflows")]
    [InlineData("/api/methods")]
    [InlineData("/api/reports")]
    [InlineData("/api/session")]
    [InlineData("/api/mcp")]
    public async Task HostedBoundary_RejectsAnonymousAndForgedPlatformIdentity(string path)
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("x-ms-client-principal",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"userId":"admin","userRoles":["admin"],"userDetails":"a@example.org"}""")));
        using var response = await client.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=", response.Headers.WwwAuthenticate.ToString());
        Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("unsigned")]
    [InlineData("wrong-key")]
    [InlineData("no-tenant")]
    [InlineData("invalid-oid")]
    [InlineData("empty-oid")]
    [InlineData("app")]
    [InlineData("bad-algorithm")]
    public async Task HostedBoundary_RejectsInvalidCryptographicAndHumanIdentity(string failure)
    {
        var token = failure switch
        {
            "expired" => Token(expires: DateTime.UtcNow.AddMinutes(-5)),
            "wrong-audience" => Token(audience: Guid.NewGuid().ToString()),
            "wrong-issuer" => Token(issuer: "https://attacker.example"),
            "unsigned" => Token(signed: false),
            "no-tenant" => Token(tenant: null),
            "invalid-oid" => Token(oid: "not-an-object-id"),
            "empty-oid" => Token(oid: Guid.Empty.ToString()),
            "app" => Token(idtyp: "app"),
            "bad-algorithm" => Token(algorithm: SecurityAlgorithms.RsaSha512),
            _ => Token(),
        };
        using var server = Server();
        if (failure == "wrong-key")
        {
            using var other = RSA.Create(2048);
            token = Token(signingKey: new RsaSecurityKey(other) { KeyId = _key.KeyId });
        }
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HostedMcp_AcceptsDelegatedToken_AndExecutesRealInitialize()
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        using var response = await client.PostAsync("/api/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("serverInfo", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Other.Permission")]
    public async Task HostedBoundary_RequiresDelegatedScope(string? scope)
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(scope: scope));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Admission_IsReevaluatedOnEveryRequest()
    {
        using var server = Server();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        _configuration["Entra:AllowedTenantIds"] = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public void MicrosoftToggle_DefaultDenies_EvenWhenAllowlisted()
    {
        _configuration["Entra:AllowedTenantIds"] = GalleryAuthSettings.MicrosoftTenant.ToString();
        Assert.False(GalleryAuthSettings.Read(_configuration).Admits(GalleryAuthSettings.MicrosoftTenant));
        _configuration["Entra:AllowMicrosoftTenant"] = "true";
        Assert.True(GalleryAuthSettings.Read(_configuration).Admits(GalleryAuthSettings.MicrosoftTenant));
    }

    [Theory]
    [InlineData("Entra:ApiClientId", "")]
    [InlineData("Entra:AllowedTenantIds", "")]
    [InlineData("Entra:AllowedTenantIds", "bad")]
    [InlineData("Entra:RequiredScope", "")]
    [InlineData("Entra:McpResource", "http://insecure/api/mcp")]
    [InlineData("Entra:AllowMicrosoftTenant", "invalid")]
    [InlineData("EnableMcpServer", "invalid")]
    [InlineData("EnableMcpWrites", "invalid")]
    public async Task MissingOrInvalidConfiguration_FailsClosed(string setting, string value)
    {
        _configuration[setting] = value;
        using var server = Server();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await server.CreateClient().GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task MetadataFailure_IsUnavailable_NotSignedOut()
    {
        using var server = Server(unavailable: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task ResourceMetadata_IsPublic_AndDescribesExplicitEntraRegistration()
    {
        using var server = Server();
        using var response = await server.CreateClient().GetAsync("/api/oauth-resource-metadata");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("https://gallery.example/api/mcp", body);
        Assert.Contains($"api://{Audience}/Gallery.Access", body);
        Assert.DoesNotContain("registration_endpoint", body);
    }

    [Fact]
    public async Task Challenge_AdvertisesThePublicMetadataHandler()
    {
        using var server = Server();
        using var client = server.CreateClient();
        using var rejected = await client.GetAsync("/api/session");
        var metadata = GalleryAuthSettings.Read(_configuration).MetadataUrl;
        Assert.Contains($"resource_metadata=\"{metadata}\"", rejected.Headers.WwwAuthenticate.ToString());
        using var response = await client.GetAsync(new Uri(metadata).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task BrowserOnly_McpIsUnavailable_BeforeTokenValidationOrHandler(string method)
    {
        _configuration["EnableMcpServer"] = "false";
        using var server = Server(unavailable: true);
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/mcp"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"error":"mcp_disabled"}""", await response.Content.ReadAsStringAsync());
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task BrowserOnly_MetadataIsUnavailable_AndSessionRetainsBearerBoundary()
    {
        _configuration["EnableMcpServer"] = "false";
        using var server = Server();
        using var client = server.CreateClient();
        using var metadata = await client.GetAsync("/api/oauth-resource-metadata");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, metadata.StatusCode);
        var body = await metadata.Content.ReadAsStringAsync();
        Assert.Contains("mcp_disabled", body);
        Assert.DoesNotContain("resource", body);
        using var anonymous = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains($"scope=\"api://{Audience}/Gallery.Access\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.DoesNotContain("resource_metadata", anonymous.Headers.WwwAuthenticate.ToString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(scope: "Other.Permission"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/session")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(audience: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session")).StatusCode);
    }

    public void Dispose() => _rsa.Dispose();

    private sealed class Metadata(SecurityKey key, bool unavailable) : IEntraMetadata
    {
        public Task<OpenIdConnectConfiguration> GetAsync(Guid tenant, bool refresh, CancellationToken cancellationToken)
        {
            if (unavailable) throw new HttpRequestException("Simulated provider outage");
            var configuration = new OpenIdConnectConfiguration { Issuer = $"https://login.microsoftonline.com/{tenant}/v2.0" };
            configuration.SigningKeys.Add(key);
            return Task.FromResult(configuration);
        }
    }

    private sealed class NoTools : IGalleryMcpTools
    {
        public Task<McpToolResult> ExecuteAsync(string toolName, System.Text.Json.Nodes.JsonObject? arguments,
            HttpRequest originating, CancellationToken cancellationToken) => throw new InvalidOperationException("No tools expected.");
    }
    private sealed class Audit : IMcpAuditLogger { public void Record(McpAuditRecord record) { } }
}
