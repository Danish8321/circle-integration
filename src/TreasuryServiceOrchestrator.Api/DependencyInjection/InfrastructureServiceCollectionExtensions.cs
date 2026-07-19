using Microsoft.EntityFrameworkCore;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static WebApplicationBuilder AddInfrastructurePersistence(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<TreasuryServiceOrchestratorDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("TreasuryServiceOrchestrator")));

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
