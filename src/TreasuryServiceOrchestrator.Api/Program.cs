using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Api.Middleware;
using TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.Filters.Add<ValidationActionFilter>());
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<TreasuryServiceOrchestratorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TreasuryServiceOrchestrator")));

builder.Services.Configure<CallerIdentityOptions>(
    builder.Configuration.GetSection(CallerIdentityOptions.SectionName));
builder.Services.AddScoped<HttpCallerContext>();
builder.Services.AddScoped<ICallerContext>(sp => sp.GetRequiredService<HttpCallerContext>());

builder.Services.AddScoped<ISubAccountRepository, SubAccountRepository>();
builder.Services.AddScoped<IEntityRegistrationRepository, EntityRegistrationRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<CreateSubAccountHandler>();
builder.Services.AddScoped<IValidator<CreateSubAccountCommand>, CreateSubAccountValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.Configure<CircleOptions>(builder.Configuration.GetSection(CircleOptions.SectionName));

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISubAccountGateway, FakeSubAccountGateway>();
}
else
{
    builder.Services.AddHttpClient<ISubAccountGateway, CircleSubAccountGateway>((sp, client) =>
    {
        var circleOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CircleOptions>>().Value;
        client.BaseAddress = new Uri(circleOptions.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", circleOptions.ApiKey);
    });
}

var app = builder.Build();

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
