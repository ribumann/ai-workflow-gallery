using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using WorkflowGallery.Api.Services;
using Xunit;

namespace WorkflowGallery.Api.Tests;

public class AzureAiComposerServiceTests
{
    [Fact]
    public async Task Compose_BoundsOutputAndDuration_WithoutRetryingFailedInference()
    {
        var chat = new FailingChatClient(new JsonException("Unusable response"));
        var service = new AzureAiComposerService(new FakeOpenAiClient(chat), "configured-deployment", NullLogger<AzureAiComposerService>.Instance);
        await Assert.ThrowsAsync<AiComposerUnavailableException>(() => service.ComposeAsync("Goal", []));
        Assert.Equal(1, chat.Calls);
        Assert.Equal(4096, chat.Options?.MaxOutputTokenCount);
        Assert.True(chat.Token.CanBeCanceled);
    }

    [Fact]
    public async Task Compose_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var chat = new FailingChatClient(new OperationCanceledException(cancellation.Token));
        var service = new AzureAiComposerService(new FakeOpenAiClient(chat), "configured-deployment", NullLogger<AzureAiComposerService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ComposeAsync("Goal", [], cancellation.Token));
        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task PublicCompose_UsesConfiguredOutputCapAndDeadlineWithoutRetry()
    {
        var chat = new FailingChatClient(new JsonException("Unusable response"));
        var settings = PublicComposerSettings.Read(PublicComposerTests.Config());
        var service = new AzureAiComposerService(new FakeOpenAiClient(chat), "test",
            NullLogger<AzureAiComposerService>.Instance, settings);
        await Assert.ThrowsAsync<AiComposerUnavailableException>(() => service.ComposeAsync("Goal", []));
        Assert.Equal(1, chat.Calls);
        Assert.Equal(1024, chat.Options?.MaxOutputTokenCount);
        Assert.True(chat.Token.CanBeCanceled);
    }

    private sealed class FakeOpenAiClient(ChatClient chat) : AzureOpenAIClient
    {
        public override ChatClient GetChatClient(string deploymentName) => chat;
    }

    private sealed class FailingChatClient(Exception error) : ChatClient
    {
        public int Calls { get; private set; }
        public ChatCompletionOptions? Options { get; private set; }
        public CancellationToken Token { get; private set; }

        public override Task<ClientResult<ChatCompletion>> CompleteChatAsync(
            IEnumerable<ChatMessage> messages, ChatCompletionOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Options = options;
            Token = cancellationToken;
            return Task.FromException<ClientResult<ChatCompletion>>(error);
        }
    }
}
