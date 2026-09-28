using Finance.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Errors;

/// <summary>
/// Turns exceptions into RFC 9457 problem details. Domain exceptions become 400/404/409 with
/// their message; anything else is logged and returned as a generic 500.
/// </summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is InvalidInputException invalid)
        {
            // Shaped like model validation, so clients show it next to the field (the file, say) like any other input error.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = new HttpValidationProblemDetails(
                    new Dictionary<string, string[]> { [invalid.Field] = [exception.Message] })
                {
                    Status = StatusCodes.Status400BadRequest
                }
            });
        }

        if (exception is AccountMismatchException)
        {
            // Not an error in the file: the client asks whether to import it anyway.
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Account mismatch",
                    Detail = exception.Message,
                    Extensions = { ["code"] = AccountMismatchException.Code }
                }
            });
        }

        var (status, title, detail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null)
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}
