using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class CircleIntegrationServiceCollectionExtensions
{
    public static WebApplicationBuilder AddCircleIntegration(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IWebhookTopicProcessor, ExternalEntitiesWebhookTopicProcessor>();
        builder.Services.AddScoped<IWebhookTopicProcessor, DepositsWebhookTopicProcessor>();
        builder.Services.AddScoped<IWebhookTopicProcessor, AddressBookRecipientsWebhookTopicProcessor>();
        builder.Services.AddScoped<IWebhookTopicProcessor, TransfersWebhookTopicProcessor>();
        builder.Services.AddScoped<IWebhookTopicProcessor, WireWebhookTopicProcessor>();
        builder.Services.AddScoped<IWebhookTopicProcessor, PayoutsWebhookTopicProcessor>();

        builder.Services.Configure<CircleOptions>(builder.Configuration.GetSection(CircleOptions.SectionName));
        builder.Services.Configure<CircleClientOptions>(
            builder.Configuration.GetSection(CircleClientOptions.SectionName));
        builder.Services.Configure<MockProviderOptions>(
            builder.Configuration.GetSection(MockProviderOptions.SectionName));

        var mockModeEnabled = builder.Configuration.GetValue<bool>($"{MockProviderOptions.SectionName}:Enabled");
        MockModeGuard.Validate(mockModeEnabled, builder.Environment.EnvironmentName);

        if (mockModeEnabled)
        {
            builder.AddMockCircleGateways();
        }
        else if (builder.Environment.IsDevelopment())
        {
            // Both Circle gateways fake together (audit F2) — a fake sub-account gateway paired with the
            // real mint gateway would issue live money-moving calls against sub-accounts Circle never saw.
            builder.Services.AddScoped<ISubAccountGateway, FakeSubAccountGateway>();
            builder.Services.AddScoped<IStablecoinGateway, FakeStablecoinGateway>();
        }
        else
        {
            builder.Services.AddHttpClient<ISubAccountGateway, CircleSubAccountGateway>(ConfigureCircleClient)
                .AddCircleResilienceHandler();
            builder.Services.AddHttpClient<IStablecoinGateway, CircleMintGateway>(ConfigureCircleClient)
                .AddCircleResilienceHandler();
        }

        // Structural guard (invariant 9's pattern): the always-accepting mock verifier is only
        // reachable in mock mode, which MockModeGuard already makes impossible in Production.
        if (mockModeEnabled)
        {
            builder.Services.AddScoped<ISnsSignatureVerifier, MockSnsSignatureVerifier>();
        }
        else
        {
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpClient("AwsSnsSigningCert");
            builder.Services.AddScoped<ISnsSignatureVerifier, AwsSnsSignatureVerifier>();
        }

        return builder;
    }

    private static void AddMockCircleGateways(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ISubAccountGateway, MockSubAccountGateway>();
        builder.Services.AddScoped<IStablecoinGateway, MockStablecoinGateway>();
        builder.Services.AddSingleton<IMockRandomSource, SystemRandomSource>();
        builder.Services.AddSingleton<IMockProviderDepositLedger, MockProviderDepositLedger>();
        builder.Services.AddSingleton<MockWebhookChannel>();
        builder.Services.AddSingleton<IMockWebhookScheduler>(sp => sp.GetRequiredService<MockWebhookChannel>());
        builder.Services.AddSingleton<MockWebhookDispatcher>();
        builder.Services.AddHostedService<MockWebhookDispatchBackgroundService>();
    }

    // Shared Circle typed-client setup (base address + bearer auth) for every Circle-backed gateway.
    private static void ConfigureCircleClient(IServiceProvider sp, HttpClient client)
    {
        var circleOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CircleOptions>>().Value;
        client.BaseAddress = new Uri(circleOptions.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", circleOptions.ApiKey);
    }
}
