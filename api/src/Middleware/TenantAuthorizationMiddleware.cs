using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using WorkflowGallery.Api.Services;
using WorkflowGallery.Api.Services.Auth;

namespace WorkflowGallery.Api.Middleware;

public sealed class TenantAuthorizationMiddleware(
    GalleryRequestAuthorization authorization,
    PlaygroundWriteGuard? playgroundWriteGuard = null) : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var http = context.GetHttpContext();
        var isHttpTrigger = context.FunctionDefinition.InputBindings.Values.Any(binding =>
            string.Equals(binding.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase));
        if (http is null && !isHttpTrigger)
        {
            await next(context);
            return;
        }
        if (http is null)
            throw new InvalidOperationException("HTTP authentication boundary has no ASP.NET request context.");

        if (context.FunctionDefinition.Name == "OAuthResourceMetadata" && !authorization.PublicDemoEnabled)
        {
            await next(context);
            return;
        }

        if (!await authorization.AuthorizeAsync(http, context.CancellationToken)) return;
        if (!authorization.IsPlaygroundWrite(http.Request))
        {
            await next(context);
            return;
        }

        var maximumBytes = http.Request.Path.StartsWithSegments("/api/tasks")
            ? PlaygroundWriteGuard.MaxTaskRequestBytes
            : PlaygroundWriteGuard.MaxCatalogRequestBytes;
        if (http.Request.ContentLength > maximumBytes)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await http.Response.WriteAsJsonAsync(new { error = "playground_request_too_large" }, context.CancellationToken);
            return;
        }

        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = maximumBytes;

        var guard = playgroundWriteGuard ?? PlaygroundWriteGuard.Worker;
        var lease = guard.TryEnter(out var retryAfter);
        if (lease is null)
        {
            http.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await http.Response.WriteAsJsonAsync(new { error = "playground_write_limit" }, context.CancellationToken);
            return;
        }

        using (lease)
        {
            try
            {
                await next(context);
            }
            catch (PlaygroundStorageLimitException)
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await http.Response.WriteAsJsonAsync(new { error = "playground_upload_budget_exhausted_or_busy" },
                    context.CancellationToken);
            }
        }
    }
}
