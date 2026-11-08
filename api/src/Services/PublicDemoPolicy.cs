using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkflowGallery.Api.Data;

namespace WorkflowGallery.Api.Services;

/// <summary>Immutable opt-in deployment policy; never grants an identity or enables paid services.</summary>
public sealed class PublicDemoPolicy(bool enabled, bool playgroundWritesEnabled = false)
{
    private static readonly object RequestKey = new();
    public static PublicDemoPolicy Private { get; } = new(false);
    public bool Enabled { get; } = enabled;
    public bool PlaygroundWritesEnabled { get; } = enabled && playgroundWritesEnabled;

    public static PublicDemoPolicy FromConfiguration(IConfiguration configuration)
    {
        var value = configuration["PublicDemoEnabled"];
        if (value is null) return Private;
        if (!bool.TryParse(value, out var enabled))
            throw new InvalidOperationException("PublicDemoEnabled must be an explicit boolean.");
        return new(enabled, ReadSwitch(configuration, "PublicPlaygroundWritesEnabled"));
    }

    private static bool ReadSwitch(IConfiguration configuration, string name)
    {
        var value = configuration[name];
        if (value is null) return false;
        if (!bool.TryParse(value, out var enabled))
            throw new InvalidOperationException($"{name} must be an explicit boolean.");
        return enabled;
    }

    public static PublicDemoPolicy For(HttpRequest request) =>
        request.HttpContext.Items.TryGetValue(RequestKey, out var policy) && policy is PublicDemoPolicy found
            ? found : request.HttpContext.RequestServices?.GetService<PublicDemoPolicy>() ?? Private;

    public void Stamp(HttpContext context)
    {
        context.Items[RequestKey] = this;
        if (!Enabled) return;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        var db = context.RequestServices?.GetService<AppDbContext>();
        if (db is not null) db.PublicOnly = true;
    }

    public bool AllowsRead(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path.Value?.TrimEnd('/') ?? "";
        if (path.Equals("/api/session", StringComparison.OrdinalIgnoreCase)) return true;
        if (!CollectionRequestContext.TryResolve(request, this, out var context)) return false;
        if (path.Equals("/api/catalog", StringComparison.OrdinalIgnoreCase)) return true;
        if (Regex.IsMatch(path, @"^/api/(tasks|workflows|methods)/[0-9a-fA-F-]{36}/(comments|rating)$", RegexOptions.IgnoreCase))
            return context!.Capabilities.CanReadEngagement;
        return Regex.IsMatch(path, @"^/api/(tasks|workflows|methods|reports)(/[0-9a-fA-F-]{36})?$", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(path, @"^/api/tasks/[0-9a-fA-F-]{36}/file$", RegexOptions.IgnoreCase);
    }

    public bool AllowsWrite(HttpRequest request)
    {
        if (!Enabled || !CollectionRequestContext.TryResolve(request, this, out var context) ||
            context!.Capabilities is not { CanWrite: true })
            return false;

        var path = request.Path.Value?.TrimEnd('/') ?? "";
        var catalogWrite = (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) ||
                            HttpMethods.IsDelete(request.Method)) &&
            Regex.IsMatch(path, @"^/api/(tasks|workflows|methods|reports)(/[0-9a-fA-F-]{36})?$", RegexOptions.IgnoreCase);
        var engagementWrite =
            request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(path, @"^/api/(tasks|workflows|methods)/[0-9a-fA-F-]{36}/comments$", RegexOptions.IgnoreCase) ||
            request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(path, @"^/api/(tasks|workflows|methods)/[0-9a-fA-F-]{36}/rating$", RegexOptions.IgnoreCase);

        return catalogWrite || engagementWrite;
    }

    public static IActionResult Denied() => new ObjectResult(new { error = "public_demo_read_only" })
        { StatusCode = StatusCodes.Status403Forbidden };
}
