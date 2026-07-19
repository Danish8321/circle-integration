using System.Globalization;
using Serilog;
using Serilog.Events;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class LoggingServiceCollectionExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, _, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "TreasuryServiceOrchestrator")
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                "logs/treasury-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture));

        return builder;
    }
}
