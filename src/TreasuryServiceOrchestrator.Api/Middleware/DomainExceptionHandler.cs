using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = MapToProblemDetails(exception);

        if (problemDetails is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static ProblemDetails? MapToProblemDetails(Exception exception) => exception switch
    {
        ValidationException validationException => new ProblemDetails
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Detail = string.Join("; ", validationException.Errors.Select(e => e.ErrorMessage)),
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        },
        TenantForbiddenException forbidden => new ProblemDetails
        {
            Title = "Forbidden.",
            Status = StatusCodes.Status403Forbidden,
            Detail = forbidden.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        },
        NotFoundException notFound => new ProblemDetails
        {
            Title = "Resource not found.",
            Status = StatusCodes.Status404NotFound,
            Detail = notFound.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        },
        SubAccountAlreadyExistsException conflict => new ProblemDetails
        {
            Title = "Sub-account already exists.",
            Status = StatusCodes.Status409Conflict,
            Detail = conflict.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        },
        ConflictException conflict => new ProblemDetails
        {
            Title = "Conflict with the current resource state.",
            Status = StatusCodes.Status409Conflict,
            Detail = conflict.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
        },
        ProviderRejectedException rejected => new ProblemDetails
        {
            Title = "The payment provider rejected the request.",
            Status = StatusCodes.Status422UnprocessableEntity,
            Detail = rejected.Message,
            Type = "https://tools.ietf.org/html/rfc4918#section-11.2",
        },
        ProviderUnavailableException unavailable => new ProblemDetails
        {
            Title = "The payment provider is temporarily unavailable.",
            Status = StatusCodes.Status503ServiceUnavailable,
            Detail = unavailable.Message,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
        },
        _ => null,
    };
}
