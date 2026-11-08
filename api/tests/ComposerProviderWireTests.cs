using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using WorkflowGallery.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace WorkflowGallery.Api.Tests;

public class ComposerProviderWireTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true, 1024)]
    [InlineData(true, 2048)]
    [InlineData(false, 4096)]
    public async Task Composer_UsesExactTokenCapAndJsonFormat_WithoutSamplingOrNetwork(bool isPublic, int tokenCap)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var sdk = new AzureOpenAIClient(new Uri("https://provider.example"), new AzureKeyCredential("test-not-a-real-key"),
            new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21)
            {
                Transport = new HttpClientPipelineTransport(http),
                RetryPolicy = new ClientRetryPolicy(0),
            });
        var settings = isPublic ? PublicComposerSettings.Read(PublicComposerTests.Config("PublicComposer:MaxOutputTokens", tokenCap.ToString())) : null;
        var service = new AzureAiComposerService(sdk, "gpt-5.4-mini",
            NullLogger<AzureAiComposerService>.Instance, settings);
        await service.ComposeAsync("Fictional local test goal", []);
        Assert.Equal(1, handler.Calls);
        Assert.NotNull(handler.Body);
        using var body = JsonDocument.Parse(handler.Body);
        output.WriteLine("Actual SDK wire (excluding messages): " +
            string.Join(", ", body.RootElement.EnumerateObject().Where(x => x.Name != "messages")
                .Select(x => $"{x.Name}={x.Value.GetRawText()}")));
        output.WriteLine("URI: " + handler.Uri);
        Assert.Equal(tokenCap, body.RootElement.GetProperty(isPublic ? "max_completion_tokens" : "max_tokens").GetInt32());
        Assert.False(body.RootElement.TryGetProperty(isPublic ? "max_tokens" : "max_completion_tokens", out _));
        Assert.Equal("https://provider.example/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-10-21", handler.Uri!.AbsoluteUri);
        Assert.Equal("gpt-5.4-mini", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(new[] { "max_completion_tokens", "messages", "model", "response_format" }
                .Select(x => isPublic ? x : x.Replace("max_completion_tokens", "max_tokens")).OrderBy(x => x),
            body.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("top_p", out _));
    }

    [Fact]
    public async Task ManagedIdentityWire_RetainsCognitiveServicesAudienceAndDeploymentEndpoint()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var credential = new LocalCredential();
        var sdk = new AzureOpenAIClient(new Uri("https://provider.cognitiveservices.azure.com"), credential,
            new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21)
            {
                Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0),
            });
        var service = new AzureAiComposerService(sdk, "gpt-5.4-mini",
            NullLogger<AzureAiComposerService>.Instance, PublicComposerSettings.Read(PublicComposerTests.Config()));
        await service.ComposeAsync("Fictional local goal", []);
        Assert.Equal("https://cognitiveservices.azure.com/.default", Assert.Single(credential.Scopes!));
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.False(handler.HasApiKey);
        Assert.Equal("https://provider.cognitiveservices.azure.com/openai/deployments/gpt-5.4-mini/chat/completions?api-version=2024-10-21",
            handler.Uri!.AbsoluteUri);
        Assert.Equal(1, handler.Calls);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(1024, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Theory]
    [InlineData(400, "unsupported_parameter", "max_tokens", "unsupported_parameter", "max_tokens")]
    [InlineData(400, "unsupported_value", "max_completion_tokens", "unsupported_value", "max_completion_tokens")]
    [InlineData(429, "rate_limit_exceeded", null, "rate_limit_exceeded", "unavailable")]
    [InlineData(500, "server_error", null, "server_error", "unavailable")]
    [InlineData(400, "SECRET-provider-input", "SECRET-provider-input", "unrecognized", "unrecognized")]
    public async Task ProviderErrors_LogOnlyStatusAndAllowlistedFields_AndNeverRetry(
        int status, string code, string? param, string expectedCode, string expectedParam)
    {
        const string sentinel = "SECRET-provider-input";
        var handler = new RecordingHandler((HttpStatusCode)status, JsonSerializer.Serialize(new
        {
            error = new { code, param, message = sentinel, type = sentinel, innererror = new { prompt = sentinel } }
        }));
        using var http = new HttpClient(handler);
        var logger = new RecordingLogger();
        var sdk = Client(http);
        var service = new AzureAiComposerService(sdk, "gpt-5.4-mini", logger,
            PublicComposerSettings.Read(PublicComposerTests.Config()));
        var error = await Assert.ThrowsAsync<AiComposerUnavailableException>(() => service.ComposeAsync(sentinel, []));
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(sentinel, error.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(sentinel, entry.Text);
        Assert.Equal(status, entry.Fields["Status"]);
        Assert.Equal(expectedCode, entry.Fields["ProviderCode"]);
        Assert.Equal(expectedParam, entry.Fields["ProviderParameter"]);
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(entry.Fields));
    }

    [Theory]
    [InlineData("SECRET-provider-input")]
    [InlineData("""{"error":"SECRET-provider-input"}""")]
    [InlineData("""{"error":{"code":42,"param":{},"message":"SECRET-provider-input"}}""")]
    [InlineData("""["SECRET-provider-input"]""")]
    public async Task MalformedProviderError_LogsNoBodyOrEchoedMessage(string errorBody)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, errorBody);
        using var http = new HttpClient(handler);
        var logger = new RecordingLogger();
        var service = new AzureAiComposerService(Client(http), "gpt-5.4-mini", logger,
            PublicComposerSettings.Read(PublicComposerTests.Config()));
        await Assert.ThrowsAsync<AiComposerUnavailableException>(() => service.ComposeAsync("SECRET-provider-input", []));
        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Equal(400, entry.Fields["Status"]);
        Assert.Equal("unavailable", entry.Fields["ProviderCode"]);
        Assert.Equal("unavailable", entry.Fields["ProviderParameter"]);
        Assert.DoesNotContain("SECRET-provider-input", entry.Text + JsonSerializer.Serialize(entry.Fields));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OversizedProviderError_DoesNotParseOrLogPayload()
    {
        await MalformedProviderError_LogsNoBodyOrEchoedMessage(JsonSerializer.Serialize(new
        {
            error = new { code = "unsupported_parameter", param = "max_tokens", message = new string('x', 8192) + "SECRET-provider-input" }
        }));
    }

    private static AzureOpenAIClient Client(HttpClient http) => new(new Uri("https://provider.example"),
        new AzureKeyCredential("SECRET-provider-input"),
        new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21)
        {
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        });

    private sealed class RecordingLogger : ILogger<AzureAiComposerService>
    {
        public List<(string Text, Exception? Exception, Dictionary<string, object?> Fields)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception,
                state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary(x => x.Key, x => x.Value) : []));
    }

    private sealed class LocalCredential : TokenCredential
    {
        public string[]? Scopes { get; private set; }
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken("SECRET-provider-input", DateTimeOffset.UtcNow.AddHours(1));
        }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string? errorBody = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public bool HasApiKey { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            HasApiKey = request.Headers.Contains("api-key");
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (errorBody is not null)
                return new HttpResponseMessage(status) { Content = new StringContent(errorBody, Encoding.UTF8, "application/json") };
            var proposal = JsonSerializer.Serialize(new
            {
                suggestedName = "Local proposal", problemStatement = "Fictional problem",
                suggestedOutcome = "Fictional outcome", steps = Array.Empty<object>(), gaps = Array.Empty<object>()
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "chatcmpl-local", @object = "chat.completion", created = 1700000000, model = "gpt-5.4-mini",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = proposal }, finish_reason = "stop" } },
                    usage = new { prompt_tokens = 20, completion_tokens = 30, total_tokens = 50 }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
