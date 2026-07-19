using Scalar.AspNetCore;
using Serilog;
using TreasuryServiceOrchestrator.Api.DependencyInjection;
using TreasuryServiceOrchestrator.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

await builder.AddProductionSecretsAsync();

builder.AddSerilogLogging();
builder.AddObservability();
builder.AddWebApiCore();
builder.AddInfrastructurePersistence();
builder.AddApplicationHandlers();
builder.AddCircleIntegration();
builder.AddBackgroundServices();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseMiddleware<CallerIdentityMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

await app.RunAsync();
