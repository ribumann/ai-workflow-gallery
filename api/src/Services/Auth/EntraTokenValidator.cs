using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace WorkflowGallery.Api.Services.Auth;

public enum GalleryAuthStatus { Success, Unauthorized, Forbidden }
public sealed record GalleryAuthResult(GalleryAuthStatus Status, ClaimsPrincipal? Principal = null);

public interface IEntraTokenValidator
{
    Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken);
}

public interface IEntraMetadata
{
    Task<OpenIdConnectConfiguration> GetAsync(Guid tenant, bool refresh, CancellationToken cancellationToken);
}

public sealed class EntraMetadata : IEntraMetadata
{
    private readonly ConcurrentDictionary<Guid, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new();
    private readonly ConcurrentDictionary<Guid, RefreshState> _refreshes = new();

    public async Task<OpenIdConnectConfiguration> GetAsync(Guid tenant, bool refresh, CancellationToken cancellationToken)
    {
        var manager = _managers.GetOrAdd(tenant, id => new(
            $"https://login.microsoftonline.com/{id}/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever()));
        if (!refresh) return await manager.GetConfigurationAsync(cancellationToken);

        // ConfigurationManager refresh is background-only once cached. Await one bounded,
        // coalesced retrieval so key rollover is not incorrectly reported as expired sign-in.
        var state = _refreshes.GetOrAdd(tenant, _ => new RefreshState());
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow - state.AttemptedAt > TimeSpan.FromMinutes(1))
            {
                state.AttemptedAt = DateTimeOffset.UtcNow;
                state.Configuration = null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                state.Retrieval = OpenIdConnectConfigurationRetriever.GetAsync(
                    $"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration",
                    new HttpDocumentRetriever(), timeout.Token);
                state.Configuration = await state.Retrieval;
                manager.RequestRefresh();
            }
            // A failed retrieval remains a visible provider failure during the throttle window.
            return state.Configuration ?? await (state.Retrieval ??
                throw new InvalidOperationException("Entra signing-key refresh has not started."));
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private sealed class RefreshState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTimeOffset AttemptedAt { get; set; } = DateTimeOffset.MinValue;
        public Task<OpenIdConnectConfiguration>? Retrieval { get; set; }
        public OpenIdConnectConfiguration? Configuration { get; set; }
    }
}

public sealed class EntraTokenValidator(IEntraMetadata metadata) : IEntraTokenValidator
{
    private readonly JsonWebTokenHandler _handler = new() { MaximumTokenSizeInBytes = 32768, MapInboundClaims = false };

    public async Task<GalleryAuthResult> ValidateAsync(string? token, GalleryAuthSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32768 || !_handler.CanReadToken(token))
            return new(GalleryAuthStatus.Unauthorized);

        JsonWebToken untrusted;
        try
        {
            untrusted = _handler.ReadJsonWebToken(token);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            return new(GalleryAuthStatus.Unauthorized);
        }
        if (!untrusted.TryGetClaim("tid", out var tenantClaim) ||
            !Guid.TryParse(tenantClaim.Value, out var tenant) || tenant == Guid.Empty)
            return new(GalleryAuthStatus.Unauthorized);

        // This is only an early denial, never an identity assertion. Do not fetch metadata for
        // arbitrary unadmitted directories supplied by an unauthenticated caller.
        if (!settings.Admits(tenant)) return new(GalleryAuthStatus.Forbidden);

        var issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
        TokenValidationResult result = null!;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var configuration = await metadata.GetAsync(tenant, attempt > 0, cancellationToken);
            if (!string.Equals(configuration.Issuer, issuer, StringComparison.Ordinal) || configuration.SigningKeys.Count == 0)
                throw new InvalidOperationException("Entra metadata issuer or signing keys are unavailable.");

            result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidAudience = settings.Audience,
                ValidIssuer = issuer,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromMinutes(1),
                AuthenticationType = GalleryIdentity.AuthenticationType,
                NameClaimType = "name",
                RoleClaimType = "roles",
            });
            if (result.IsValid || result.Exception is not SecurityTokenSignatureKeyNotFoundException) break;
        }

        if (!result.IsValid) return new(GalleryAuthStatus.Unauthorized);
        var identity = result.ClaimsIdentity;
        if (identity.FindFirst("ver")?.Value != "2.0" ||
            !Guid.TryParse(identity.FindFirst("tid")?.Value, out var verifiedTenant) || verifiedTenant != tenant ||
            !Guid.TryParse(identity.FindFirst("oid")?.Value, out var objectId) || objectId == Guid.Empty ||
            identity.FindFirst("idtyp")?.Value == "app")
            return new(GalleryAuthStatus.Unauthorized);

        if (identity.FindFirst("scp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(settings.Scope, StringComparer.Ordinal) != true)
            return new(GalleryAuthStatus.Forbidden);

        return new(GalleryAuthStatus.Success, new ClaimsPrincipal(identity));
    }
}
