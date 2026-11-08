using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WorkflowGallery.Api.Middleware;
using WorkflowGallery.Api.Services.Auth;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public sealed class TenantAuthorizationAdapterTests
{
    private static TenantAuthorizationMiddleware Middleware(GalleryAuthStatus status = GalleryAuthStatus.Success, bool mcpEnabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entra:ApiClientId"] = "22222222-2222-4222-8222-222222222222",
            ["Entra:RequiredScope"] = "Gallery.Access",
            ["Entra:AllowedTenantIds"] = "11111111-1111-4111-8111-111111111111",
            ["Entra:McpResource"] = "https://gallery.example/api/mcp",
            ["EnableMcpServer"] = mcpEnabled.ToString(),
        }).Build();
        return new(new GalleryRequestAuthorization(configuration, new Validator(status),
            NullLogger<GalleryRequestAuthorization>.Instance));
    }

    [Fact]
    public async Task NonHttpInvocation_PassesWithoutRequestContext()
    {
        var context = new Context("Timer", "timerTrigger");
        var called = false;
        await Middleware().Invoke(context, _ => { called = true; return Task.CompletedTask; });
        Assert.True(called);
    }

    [Fact]
    public async Task MissingHttpContext_FailsClosedBeforeHandler()
    {
        var context = new Context("Tasks", "httpTrigger");
        var called = false;
        var loggingBoundary = new ExceptionHandlingMiddleware(NullLogger<ExceptionHandlingMiddleware>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            loggingBoundary.Invoke(context, invocation =>
                Middleware().Invoke(invocation, _ => { called = true; return Task.CompletedTask; })));
        Assert.False(called);
    }

    [Fact]
    public async Task HttpInvocation_PassesValidatedIdentityToHandler()
    {
        var http = new DefaultHttpContext();
        var context = new Context("Tasks", "httpTrigger", http);
        ClaimsPrincipal? observed = null;
        await Middleware().Invoke(context, invocation =>
        {
            observed = invocation.GetHttpContext()!.User;
            return Task.CompletedTask;
        });
        Assert.NotNull(observed);
        Assert.Equal(GalleryIdentity.AuthenticationType, observed.Identity!.AuthenticationType);
        Assert.NotNull(GalleryIdentity.Key(http.Request));
    }

    [Theory]
    [InlineData(GalleryAuthStatus.Unauthorized, 401)]
    [InlineData(GalleryAuthStatus.Forbidden, 403)]
    public async Task DeniedHttpInvocation_NeverExecutesHandler(GalleryAuthStatus status, int expected)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var context = new Context("McpPost", "httpTrigger", http);
        var called = false;
        await Middleware(status).Invoke(context, _ => { called = true; return Task.CompletedTask; });
        Assert.False(called);
        Assert.Equal(expected, http.Response.StatusCode);
        Assert.Null(GalleryIdentity.Key(http.Request));
    }

    [Theory]
    [InlineData("OAuthResourceMetadata", true)]
    [InlineData("GetSession", false)]
    [InlineData("McpPost", false)]
    public async Task OnlyMetadataHttpFunction_IsPublic(string name, bool allowed)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var context = new Context(name, "httpTrigger", http);
        var called = false;
        await Middleware(GalleryAuthStatus.Unauthorized).Invoke(context, _ => { called = true; return Task.CompletedTask; });
        Assert.Equal(allowed, called);
    }

    [Theory]
    [InlineData("McpEndpoint", "POST")]
    [InlineData("McpEndpointGet", "GET")]
    [InlineData("McpEndpointDelete", "DELETE")]
    public async Task ActualWorkerAdapter_DisabledMcpNeverInvokesHandler(string name, string method)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/mcp";
        http.Request.Method = method;
        http.Response.Body = new MemoryStream();
        var called = false;
        await Middleware(mcpEnabled: false).Invoke(new Context(name, "httpTrigger", http),
            _ => { called = true; return Task.CompletedTask; });
        Assert.False(called);
        Assert.Equal(503, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        Assert.Contains("mcp_disabled", await new StreamReader(http.Response.Body).ReadToEndAsync());
        Assert.Null(GalleryIdentity.Key(http.Request));
    }

    private sealed class Validator(GalleryAuthStatus status) : IEntraTokenValidator
    {
        public Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("tid", "11111111-1111-4111-8111-111111111111"),
                new Claim("oid", "33333333-3333-4333-8333-333333333333"),
            ], GalleryIdentity.AuthenticationType));
            return Task.FromResult(new GalleryAuthResult(status, status == GalleryAuthStatus.Success ? principal : null));
        }
    }

    private sealed class Context(string name, string trigger, HttpContext? http = null) : FunctionContext
    {
        public override string InvocationId => "adapter-test";
        public override string FunctionId => name;
        public override TraceContext TraceContext => throw new NotSupportedException();
        public override BindingContext BindingContext => throw new NotSupportedException();
        public override RetryContext RetryContext => throw new NotSupportedException();
        public override IServiceProvider InstanceServices { get; set; } = new EmptyServices();
        public override FunctionDefinition FunctionDefinition { get; } = new Definition(name, trigger);
        public override IDictionary<object, object> Items { get; set; } = http is null
            ? new Dictionary<object, object>()
            : new Dictionary<object, object> { ["HttpRequestContext"] = http };
        public override IInvocationFeatures Features => throw new NotSupportedException();
    }

    private sealed class Definition(string name, string trigger) : FunctionDefinition
    {
        public override ImmutableArray<FunctionParameter> Parameters => [];
        public override string PathToAssembly => "";
        public override string EntryPoint => name;
        public override string Id => name;
        public override string Name => name;
        public override IImmutableDictionary<string, BindingMetadata> InputBindings { get; } =
            ImmutableDictionary<string, BindingMetadata>.Empty.Add("trigger", new Binding(trigger));
        public override IImmutableDictionary<string, BindingMetadata> OutputBindings =>
            ImmutableDictionary<string, BindingMetadata>.Empty;
    }

    private sealed class Binding(string type) : BindingMetadata
    {
        public override string Name => "trigger";
        public override string Type => type;
        public override BindingDirection Direction => BindingDirection.In;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
