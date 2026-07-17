using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class CallerIdentityMiddleware(RequestDelegate next)
{
    private const string HeaderName = "ClientCompanyId";

    public async Task InvokeAsync(HttpContext context, HttpCallerContext callerContext, IOptions<CallerIdentityOptions> options)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var callerId) || string.IsNullOrWhiteSpace(callerId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var role = string.Equals(callerId.ToString(), options.Value.AdminCallerId, StringComparison.Ordinal)
            ? CallerRole.Admin
            : CallerRole.SubAccount;

        callerContext.Set(callerId.ToString(), role);

        await next(context);
    }
}
