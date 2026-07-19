using TreasuryServiceOrchestrator.Api.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.AddWebApiCore();
builder.AddInfrastructurePersistence();
builder.AddApplicationHandlers();
builder.AddCircleIntegration();
builder.AddBackgroundServices();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseMiddleware<CallerIdentityMiddleware>();

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
