using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class ObservabilityServiceCollectionExtensions
{
    private const string ServiceName = "TreasuryServiceOrchestrator";

    /// <summary>
    /// OpenTelemetry tracing is a feature flag (<see cref="OpenTelemetryOptions.Enabled"/>), not a
    /// hard requirement — disabled means this method wires nothing at all, no collector needed to
    /// run the API. See docs/features/05-reliability-and-error-handling.md.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpenTelemetryOptions>(
            builder.Configuration.GetSection(OpenTelemetryOptions.SectionName));

        var options = builder.Configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        if (!options.Enabled)
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(options.OtlpEndpoint)));

        return builder;
    }
}
