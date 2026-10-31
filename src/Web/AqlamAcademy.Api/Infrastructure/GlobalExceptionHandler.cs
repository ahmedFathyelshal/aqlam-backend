using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Infrastructure;

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Validation failures are client errors: return 400 with field-keyed errors
        // so the frontend can show them under the matching inputs.
        if (exception is ValidationException validationException)
        {
            var errors = validationException.Errors
                .GroupBy(e => ToCamelCase(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                },
                Exception = exception
            });
        }

        logger.LogError(exception, "Unhandled exception occurred: {Message}", exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Server Error",
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
                // Never leak internals outside Development
                Detail = environment.IsDevelopment() ? exception.Message : "An unexpected error occurred."
            },
            Exception = exception
        });
    }

    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName) ? string.Empty : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
