namespace TreasuryServiceOrchestrator.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
