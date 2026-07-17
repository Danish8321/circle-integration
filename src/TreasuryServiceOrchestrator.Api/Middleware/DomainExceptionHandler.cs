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
        var problemDetails = exception switch
        {
            ValidationException validationException => new ProblemDetails
            {
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Detail = string.Join("; ", validationException.Errors.Select(e => e.ErrorMessage)),
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            },
            SubAccountAlreadyExistsException conflict => new ProblemDetails
            {
                Title = "Sub-account already exists.",
                Status = StatusCodes.Status409Conflict,
                Detail = conflict.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            },
            _ => null,
        };

        if (problemDetails is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problemDetails.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
