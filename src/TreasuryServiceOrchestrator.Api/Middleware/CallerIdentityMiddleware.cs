using Microsoft.Extensions.Options;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class CallerIdentityMiddleware(RequestDelegate next)
{
    private const string HeaderName = "ClientCompanyId";

    public async Task InvokeAsync(
        HttpContext context,
        HttpCallerContext callerContext,
        IOptions<CallerIdentityOptions> options,
        ISubAccountRepository subAccountRepository)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var callerId) || string.IsNullOrWhiteSpace(callerId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var callerIdValue = callerId.ToString();

        if (string.Equals(callerIdValue, options.Value.AdminCallerId, StringComparison.Ordinal))
        {
            callerContext.Set(callerIdValue, CallerRole.Admin);
            await next(context);
            return;
        }

        var subAccount = await subAccountRepository.GetByClientCompanyIdAsync(callerIdValue, context.RequestAborted);
        if (subAccount is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        callerContext.Set(callerIdValue, CallerRole.SubAccount);

        await next(context);
    }
}
