using TreasuryServiceOrchestrator.Infrastructure.Notifications;
using TreasuryServiceOrchestrator.Infrastructure.Reconciliation;
using TreasuryServiceOrchestrator.Infrastructure.Snapshots;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class BackgroundServiceCollectionExtensions
{
    public static WebApplicationBuilder AddBackgroundServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<NotificationDispatcherOptions>(
            builder.Configuration.GetSection(NotificationDispatcherOptions.SectionName));
        builder.Services.AddHttpClient<INotificationSender, HttpNotificationSender>();
        builder.Services.AddSingleton<NotificationDispatcher>();
        builder.Services.AddHostedService<NotificationDispatchBackgroundService>();

        builder.Services.Configure<ReconciliationOptions>(builder.Configuration.GetSection("Reconciliation"));
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReconciliationOptions>>().Value);
        builder.Services.AddScoped<DepositReconciliationService>();
        builder.Services.AddHostedService<DepositReconciliationBackgroundService>();

        builder.Services.Configure<BalanceSnapshotOptions>(builder.Configuration.GetSection("BalanceSnapshot"));
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BalanceSnapshotOptions>>().Value);
        builder.Services.AddScoped<ScheduledBalanceSnapshotService>();
        builder.Services.AddHostedService<ScheduledBalanceSnapshotBackgroundService>();

        return builder;
    }
}
