using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wings.Application.Common;

namespace Wings.Api.Infrastructure;

/// <summary>Maps application exceptions to RFC 7807 problem responses; unexpected errors are logged and returned without internals.</summary>
public sealed class AppExceptionHandler(IProblemDetailsService problems, ILogger<AppExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            BadHttpRequestException b => (b.StatusCode, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };
        if (status == 500) log.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);

        var details = exception is ValidationException v
            ? new ValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value)) { Title = title, Detail = v.Message, Status = status }
            : new ProblemDetails { Title = title, Status = status, Detail = status != 500 ? exception.Message
                : env.IsDevelopment() || env.IsEnvironment("Testing") ? exception.ToString() : "An unexpected error occurred. It has been logged." };
        details.Extensions["traceId"] = http.TraceIdentifier;
        http.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}
