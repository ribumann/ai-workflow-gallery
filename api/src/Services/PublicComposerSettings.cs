using Microsoft.Extensions.Configuration;

namespace WorkflowGallery.Api.Services;

/// <summary>Public inference is disabled unless every setting is explicitly valid.</summary>
public sealed record PublicComposerSettings
{
    public bool OptedIn { get; init; }
    public bool Ready { get; init; }
    public int DailyRequestLimit { get; init; }
    public int DailyTokenLimit { get; init; }
    public int MaxConcurrentRequests { get; init; }
    public int MaxGoalCharacters { get; init; }
    public int MaxContextBytes { get; init; }
    public int MaxOutputTokens { get; init; }
    public int ModelTimeoutSeconds { get; init; }
    public const int LeaseSeconds = 300;
    public const int SystemPromptAllowanceBytes = 4096;

    // UTF-8 bytes upper-bound text tokens, with extra allowance for chat framing.
    // MaxOutputTokens also bounds reasoning tokens; reservations are never refunded.
    public int ReservedTokens => checked(MaxContextBytes + 4 * MaxGoalCharacters +
        SystemPromptAllowanceBytes + 1024 + MaxOutputTokens);

    public static PublicComposerSettings Read(IConfiguration configuration)
    {
        var optedIn = string.Equals(configuration["PublicComposerEnabled"], "true", StringComparison.OrdinalIgnoreCase);
        var valid = bool.TryParse(configuration["EnableAiComposer"], out var enabled) && enabled &&
            Uri.TryCreate(configuration["AiFoundryEndpoint"], UriKind.Absolute, out var endpoint) &&
            endpoint.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(endpoint.UserInfo) &&
            !string.IsNullOrWhiteSpace(configuration["AiFoundryDeploymentName"]);
        int ReadLimit(string name, int maximum)
        {
            if (int.TryParse(configuration[$"PublicComposer:{name}"], out var value) && value >= 1 && value <= maximum)
                return value;
            valid = false;
            return 0;
        }
        var settings = new PublicComposerSettings
        {
            OptedIn = optedIn,
            DailyRequestLimit = ReadLimit(nameof(DailyRequestLimit), 100),
            DailyTokenLimit = ReadLimit(nameof(DailyTokenLimit), 2_500_000),
            MaxConcurrentRequests = ReadLimit(nameof(MaxConcurrentRequests), 2),
            MaxGoalCharacters = ReadLimit(nameof(MaxGoalCharacters), 1000),
            MaxContextBytes = ReadLimit(nameof(MaxContextBytes), 16000),
            MaxOutputTokens = ReadLimit(nameof(MaxOutputTokens), 2048),
            ModelTimeoutSeconds = ReadLimit(nameof(ModelTimeoutSeconds), 30),
        };
        return settings with { Ready = optedIn && valid &&
            AzureAiComposerService.SystemPromptBytes <= SystemPromptAllowanceBytes &&
            settings.DailyTokenLimit >= settings.ReservedTokens };
    }

    public static bool IsProposalRequest(Microsoft.AspNetCore.Http.HttpRequest request) =>
        Microsoft.AspNetCore.Http.HttpMethods.IsPost(request.Method) &&
        request.Path.Equals("/api/ai/compose", StringComparison.OrdinalIgnoreCase);
}
