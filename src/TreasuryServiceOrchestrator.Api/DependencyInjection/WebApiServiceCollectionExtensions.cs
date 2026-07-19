using TreasuryServiceOrchestrator.Api.Middleware;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    public static WebApplicationBuilder AddWebApiCore(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers(options => options.Filters.Add<ValidationActionFilter>());
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.Configure<CallerIdentityOptions>(
            builder.Configuration.GetSection(CallerIdentityOptions.SectionName));
        builder.Services.AddScoped<HttpCallerContext>();
        builder.Services.AddScoped<ICallerContext>(sp => sp.GetRequiredService<HttpCallerContext>());
        builder.Services.AddScoped<ISettableCallerContext>(sp => sp.GetRequiredService<HttpCallerContext>());

        return builder;
    }
}
