using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static WebApplicationBuilder AddInfrastructurePersistence(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("TreasuryServiceOrchestrator")
            ?? throw new InvalidOperationException("Missing connection string 'TreasuryServiceOrchestrator'.");

        builder.Services.AddDbContext<TreasuryServiceOrchestratorDbContext>(options =>
            options.UseSqlServer(connectionString));

        builder.Services.AddHealthChecks()
            .AddSqlServer(connectionString, name: "sql-server");

        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();

        builder.Services.AddScoped<ISubAccountRepository, SubAccountRepository>();
        builder.Services.AddScoped<IEntityRegistrationRepository, EntityRegistrationRepository>();
        builder.Services.AddScoped<IDepositAddressRepository, DepositAddressRepository>();
        builder.Services.AddScoped<IRecipientRepository, RecipientRepository>();
        builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
        builder.Services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();
        builder.Services.AddScoped<IFundAccountRepository, FundAccountRepository>();
        builder.Services.AddScoped<ITransferRepository, TransferRepository>();
        builder.Services.AddScoped<ILinkedBankAccountRepository, LinkedBankAccountRepository>();
        builder.Services.AddScoped<IRedeemRequestRepository, RedeemRequestRepository>();
        builder.Services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
        builder.Services.AddScoped<INotificationOutboxRepository, NotificationOutboxRepository>();

        return builder;
    }
}
