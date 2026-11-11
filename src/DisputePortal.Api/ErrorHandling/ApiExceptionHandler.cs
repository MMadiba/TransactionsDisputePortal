using System.Text.Json;
using DisputePortal.Application.Common;
using DisputePortal.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Expected failures get their proper status and a stable
/// <c>code</c>; anything else is a 500 that never leaks internals.
/// </summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var path = httpContext.Features.Get<IExceptionHandlerFeature>()?.Path ?? httpContext.Request.Path.Value ?? "/";

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true; // Client went away; nothing to write.
        }

        var problem = exception switch
        {
            RequestValidationException v => new ValidationProblemDetails(v.Errors) { Status = StatusCodes.Status400BadRequest },
            NotFoundException e => Problem(StatusCodes.Status404NotFound, "Not found", e),
            DomainConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict", e),
            BusinessRuleViolationException e => Problem(StatusCodes.Status422UnprocessableEntity, "Request can't be completed", e),
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Something went wrong on our side",
                Detail = "The error has been logged. Please try again later."
            }
        };

        if (problem.Status >= 500)
        {
            LogUnhandled(exception, httpContext.Request.Method, path);
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status!.Value;

        // Written directly: by the time exception handlers run the endpoint has been cleared, and
        // IProblemDetailsService's content negotiation can decline, silently falling through to HTML.
        await httpContext.Response.WriteAsJsonAsync<object>(problem, JsonOptions, "application/problem+json", cancellationToken);
        return true;
    }

    private static ProblemDetails Problem(int status, string title, DomainException e) =>
        new() { Status = status, Title = title, Detail = e.Message, Extensions = { ["code"] = e.Code } };

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, string path);
}
