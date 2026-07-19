using FluentValidation;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static WebApplicationBuilder AddApplicationHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<SupportedChainsOptions>(
            builder.Configuration.GetSection(SupportedChainsOptions.SectionName));
        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupportedChainsOptions>>().Value);

        builder.AddComplianceHandlers();
        builder.AddLedgerHandlers();
        builder.AddTransferAndLinkedBankAccountHandlers();
        builder.AddRedemptionAndWebhookHandlers();

        builder.Services.AddValidatorsFromAssemblyContaining<Program>();

        return builder;
    }

    private static void AddComplianceHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CreateSubAccountHandler>();
        builder.Services.AddScoped<GetSubAccountHandler>();
        builder.Services.AddScoped<ListSubAccountsHandler>();
        builder.Services.AddScoped<SetSubAccountDisabledHandler>();
        builder.Services.AddScoped<ResubmitEntityRegistrationHandler>();
        builder.Services.AddScoped<ProcessExternalEntityDecisionHandler>();
        builder.Services.AddScoped<IValidator<CreateSubAccountCommand>, CreateSubAccountValidator>();
        builder.Services.AddScoped<IValidator<ResubmitEntityRegistrationCommand>, ResubmitEntityRegistrationValidator>();

        builder.Services.AddScoped<GenerateDepositAddressCommandHandler>();
        builder.Services.AddScoped<ListDepositAddressesQueryHandler>();
        builder.Services.AddScoped<IValidator<GenerateDepositAddressCommand>, GenerateDepositAddressCommandValidator>();

        builder.Services.AddScoped<RegisterRecipientCommandHandler>();
        builder.Services.AddScoped<ListRecipientsQueryHandler>();
        builder.Services.AddScoped<GetRecipientQueryHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>, ProcessRecipientDecisionHandler>();
        builder.Services.AddScoped<IValidator<RegisterRecipientCommand>, RegisterRecipientCommandValidator>();
    }

    private static void AddLedgerHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<LedgerPostingService>();
        builder.Services.AddScoped<ListTransactionsQueryHandler>();
        builder.Services.AddScoped<GetTransactionQueryHandler>();
        builder.Services.AddScoped<GetCurrentBalanceQueryHandler>();
        builder.Services.AddScoped<GetBalanceHistoryQueryHandler>();
        builder.Services.AddScoped<ListAllTransactionsQueryHandler>();
        builder.Services.AddScoped<GetMasterAccountSummaryQueryHandler>();
        builder.Services.AddScoped<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>, ProcessDepositCommandHandler>();
        builder.Services.AddScoped<IValidator<ProcessDepositCommand>, ProcessDepositCommandValidator>();
    }

    private static void AddTransferAndLinkedBankAccountHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CreateTransferCommandHandler>();
        builder.Services.AddScoped<ListTransfersQueryHandler>();
        builder.Services.AddScoped<GetTransferQueryHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>, ProcessTransferStatusCommandHandler>();
        builder.Services.AddScoped<IValidator<CreateTransferCommand>, CreateTransferCommandValidator>();

        builder.Services.AddScoped<CreateLinkedBankAccountCommandHandler>();
        builder.Services.AddScoped<ListLinkedBankAccountsQueryHandler>();
        builder.Services.AddScoped<GetLinkedBankAccountQueryHandler>();
        builder.Services.AddScoped<GetWireInstructionsQueryHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult>,
            ProcessLinkedBankAccountStatusCommandHandler>();
        builder.Services.AddScoped<
            IValidator<CreateLinkedBankAccountCommand>, CreateLinkedBankAccountCommandValidator>();
    }

    private static void AddRedemptionAndWebhookHandlers(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CreateRedemptionCommandHandler>();
        builder.Services.AddScoped<ListRedemptionsQueryHandler>();
        builder.Services.AddScoped<GetRedemptionQueryHandler>();
        builder.Services.AddScoped<
            ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>, ProcessPayoutStatusCommandHandler>();
        builder.Services.AddScoped<IValidator<CreateRedemptionCommand>, CreateRedemptionCommandValidator>();

        builder.Services.AddScoped<WebhookProcessor>();
        builder.Services.AddScoped<ReplayWebhookInboxEntryHandler>();
        builder.Services.AddScoped<ReplayNotificationOutboxEntryHandler>();
    }
}
