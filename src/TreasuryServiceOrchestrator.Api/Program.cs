using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TreasuryServiceOrchestrator.Api.Middleware;
using TreasuryServiceOrchestrator.Application.Admin;
using TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.ListSubAccounts;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;
using TreasuryServiceOrchestrator.Application.Compliance.ResubmitEntityRegistration;
using TreasuryServiceOrchestrator.Application.Compliance.SetSubAccountDisabled;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.DepositAddresses;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Ledger.Reconciliation;
using TreasuryServiceOrchestrator.Application.Ledger.Snapshots;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Notifications;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;
using TreasuryServiceOrchestrator.Infrastructure.Reconciliation;
using TreasuryServiceOrchestrator.Infrastructure.Snapshots;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

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
builder.Services.AddScoped<ISettableCallerContext>(sp => sp.GetRequiredService<HttpCallerContext>());

builder.Services.AddScoped<ISubAccountRepository, SubAccountRepository>();
builder.Services.AddScoped<IEntityRegistrationRepository, EntityRegistrationRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<CreateSubAccountHandler>();
builder.Services.AddScoped<GetSubAccountHandler>();
builder.Services.AddScoped<ListSubAccountsHandler>();
builder.Services.AddScoped<SetSubAccountDisabledHandler>();
builder.Services.AddScoped<ResubmitEntityRegistrationHandler>();
builder.Services.AddScoped<ProcessExternalEntityDecisionHandler>();
builder.Services.AddScoped<IValidator<CreateSubAccountCommand>, CreateSubAccountValidator>();
builder.Services.AddScoped<IValidator<ResubmitEntityRegistrationCommand>, ResubmitEntityRegistrationValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.Configure<SupportedChainsOptions>(
    builder.Configuration.GetSection(SupportedChainsOptions.SectionName));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupportedChainsOptions>>().Value);
builder.Services.AddScoped<IDepositAddressRepository, DepositAddressRepository>();
builder.Services.AddScoped<GenerateDepositAddressCommandHandler>();
builder.Services.AddScoped<ListDepositAddressesQueryHandler>();
builder.Services.AddScoped<IValidator<GenerateDepositAddressCommand>, GenerateDepositAddressCommandValidator>();

builder.Services.AddScoped<IRecipientRepository, RecipientRepository>();
builder.Services.AddScoped<RegisterRecipientCommandHandler>();
builder.Services.AddScoped<ListRecipientsQueryHandler>();
builder.Services.AddScoped<GetRecipientQueryHandler>();
builder.Services.AddScoped<
    ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>, ProcessRecipientDecisionHandler>();
builder.Services.AddScoped<IValidator<RegisterRecipientCommand>, RegisterRecipientCommandValidator>();

builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();
builder.Services.AddScoped<IFundAccountRepository, FundAccountRepository>();
builder.Services.AddScoped<LedgerPostingService>();
builder.Services.AddScoped<ListTransactionsQueryHandler>();
builder.Services.AddScoped<GetTransactionQueryHandler>();
builder.Services.AddScoped<GetCurrentBalanceQueryHandler>();
builder.Services.AddScoped<GetBalanceHistoryQueryHandler>();
builder.Services.AddScoped<ListAllTransactionsQueryHandler>();
builder.Services.AddScoped<GetMasterAccountSummaryQueryHandler>();
builder.Services.AddScoped<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>, ProcessDepositCommandHandler>();
builder.Services.AddScoped<IValidator<ProcessDepositCommand>, ProcessDepositCommandValidator>();

builder.Services.AddScoped<ITransferRepository, TransferRepository>();
builder.Services.AddScoped<CreateTransferCommandHandler>();
builder.Services.AddScoped<ListTransfersQueryHandler>();
builder.Services.AddScoped<GetTransferQueryHandler>();
builder.Services.AddScoped<
    ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>, ProcessTransferStatusCommandHandler>();
builder.Services.AddScoped<IValidator<CreateTransferCommand>, CreateTransferCommandValidator>();

builder.Services.AddScoped<ILinkedBankAccountRepository, LinkedBankAccountRepository>();
builder.Services.AddScoped<CreateLinkedBankAccountCommandHandler>();
builder.Services.AddScoped<ListLinkedBankAccountsQueryHandler>();
builder.Services.AddScoped<GetLinkedBankAccountQueryHandler>();
builder.Services.AddScoped<GetWireInstructionsQueryHandler>();
builder.Services.AddScoped<
    ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult>,
    ProcessLinkedBankAccountStatusCommandHandler>();
builder.Services.AddScoped<
    IValidator<CreateLinkedBankAccountCommand>, CreateLinkedBankAccountCommandValidator>();

builder.Services.AddScoped<IRedeemRequestRepository, RedeemRequestRepository>();
builder.Services.AddScoped<CreateRedemptionCommandHandler>();
builder.Services.AddScoped<ListRedemptionsQueryHandler>();
builder.Services.AddScoped<GetRedemptionQueryHandler>();
builder.Services.AddScoped<
    ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>, ProcessPayoutStatusCommandHandler>();
builder.Services.AddScoped<IValidator<CreateRedemptionCommand>, CreateRedemptionCommandValidator>();

builder.Services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
builder.Services.AddScoped<ISnsSignatureVerifier, MockSnsSignatureVerifier>();
builder.Services.AddScoped<IWebhookTopicProcessor, ExternalEntitiesWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, DepositsWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, AddressBookRecipientsWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, TransfersWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, WireWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, PayoutsWebhookTopicProcessor>();
builder.Services.AddScoped<WebhookProcessor>();
builder.Services.AddScoped<ReplayWebhookInboxEntryHandler>();

builder.Services.Configure<CircleOptions>(builder.Configuration.GetSection(CircleOptions.SectionName));
builder.Services.Configure<CircleClientOptions>(
    builder.Configuration.GetSection(CircleClientOptions.SectionName));

builder.Services.Configure<NotificationDispatcherOptions>(
    builder.Configuration.GetSection(NotificationDispatcherOptions.SectionName));
builder.Services.AddScoped<INotificationOutboxRepository, NotificationOutboxRepository>();
builder.Services.AddScoped<ReplayNotificationOutboxEntryHandler>();
builder.Services.AddHttpClient<INotificationSender, HttpNotificationSender>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationDispatchBackgroundService>();

builder.Services.Configure<ReconciliationOptions>(
    builder.Configuration.GetSection("Reconciliation"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReconciliationOptions>>().Value);
builder.Services.AddScoped<DepositReconciliationService>();
builder.Services.AddHostedService<DepositReconciliationBackgroundService>();

builder.Services.Configure<BalanceSnapshotOptions>(
    builder.Configuration.GetSection("BalanceSnapshot"));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BalanceSnapshotOptions>>().Value);
builder.Services.AddScoped<ScheduledBalanceSnapshotService>();
builder.Services.AddHostedService<ScheduledBalanceSnapshotBackgroundService>();

builder.Services.Configure<MockProviderOptions>(
    builder.Configuration.GetSection(MockProviderOptions.SectionName));

var mockModeEnabled = builder.Configuration.GetValue<bool>($"{MockProviderOptions.SectionName}:Enabled");

MockModeGuard.Validate(mockModeEnabled, builder.Environment.EnvironmentName);

if (mockModeEnabled)
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
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISubAccountGateway, FakeSubAccountGateway>();
    builder.Services.AddHttpClient<IStablecoinGateway, CircleMintGateway>(ConfigureCircleClient)
        .AddCircleResilienceHandler();
}
else
{
    builder.Services.AddHttpClient<ISubAccountGateway, CircleSubAccountGateway>(ConfigureCircleClient)
        .AddCircleResilienceHandler();
    builder.Services.AddHttpClient<IStablecoinGateway, CircleMintGateway>(ConfigureCircleClient)
        .AddCircleResilienceHandler();
}

// Shared Circle typed-client setup (base address + bearer auth) for every Circle-backed gateway.
static void ConfigureCircleClient(IServiceProvider sp, HttpClient client)
{
    var circleOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CircleOptions>>().Value;
    client.BaseAddress = new Uri(circleOptions.BaseUrl);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", circleOptions.ApiKey);
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

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
