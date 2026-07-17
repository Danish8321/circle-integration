using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TreasuryServiceOrchestrator.Api.Middleware;
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
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Application.Webhooks.Ports;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;
using TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;
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
builder.Services.AddScoped<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>, ProcessDepositCommandHandler>();
builder.Services.AddScoped<IValidator<ProcessDepositCommand>, ProcessDepositCommandValidator>();

builder.Services.AddScoped<IWebhookInboxRepository, WebhookInboxRepository>();
builder.Services.AddScoped<ISnsSignatureVerifier, MockSnsSignatureVerifier>();
builder.Services.AddScoped<IWebhookTopicProcessor, ExternalEntitiesWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, DepositsWebhookTopicProcessor>();
builder.Services.AddScoped<IWebhookTopicProcessor, AddressBookRecipientsWebhookTopicProcessor>();
builder.Services.AddScoped<WebhookProcessor>();

builder.Services.Configure<CircleOptions>(builder.Configuration.GetSection(CircleOptions.SectionName));

builder.Services.Configure<MockProviderOptions>(
    builder.Configuration.GetSection(MockProviderOptions.SectionName));

var mockModeEnabled = builder.Configuration.GetValue<bool>($"{MockProviderOptions.SectionName}:Enabled");

MockModeGuard.Validate(mockModeEnabled, builder.Environment.EnvironmentName);

if (mockModeEnabled)
{
    builder.Services.AddScoped<ISubAccountGateway, MockSubAccountGateway>();
    builder.Services.AddScoped<IStablecoinGateway, MockStablecoinGateway>();
    builder.Services.AddSingleton<IMockRandomSource, SystemRandomSource>();
    builder.Services.AddSingleton<MockWebhookChannel>();
    builder.Services.AddSingleton<IMockWebhookScheduler>(sp => sp.GetRequiredService<MockWebhookChannel>());
    builder.Services.AddSingleton<MockWebhookDispatcher>();
    builder.Services.AddHostedService<MockWebhookDispatchBackgroundService>();
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISubAccountGateway, FakeSubAccountGateway>();
    builder.Services.AddHttpClient<IStablecoinGateway, CircleMintGateway>((sp, client) =>
    {
        var circleOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CircleOptions>>().Value;
        client.BaseAddress = new Uri(circleOptions.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", circleOptions.ApiKey);
    });
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
    builder.Services.AddHttpClient<IStablecoinGateway, CircleMintGateway>((sp, client) =>
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
