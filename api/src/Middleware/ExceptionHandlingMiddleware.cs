using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace WorkflowGallery.Api.Middleware;

/// <summary>
/// Centralized exception-handling/logging middleware for the isolated-worker Functions host
/// (tasks.md T017). Logs every unhandled exception with the invocation ID for correlation and
/// re-throws so the host's default problem-details response is still produced — this middleware
/// exists to guarantee every failure is logged exactly once in a consistent shape, not to change
/// the response contract.
/// </summary>
public class ExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in function {FunctionName} (invocation {InvocationId})",
                context.FunctionDefinition.Name,
                context.InvocationId);
            throw;
        }
    }
}
