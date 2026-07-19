using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Mocks;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.IntegrationTests;

/// <summary>
/// Ticket 10.1: the terminal Phase-1 acceptance gate. Walks the PRD §15.1 demo script start to
/// finish against tickets 01-09's actually-shipped shapes (not the doc's original draft): admin
/// creates a sub-account -&gt; screening accepted (+ a second rejected/resubmitted) -&gt; deposit
/// address generated -&gt; simulated deposit credits ledger/balance -&gt; recipient registered +
/// approved -&gt; transfer completes -&gt; redemption completes (gross/fees/net visible) -&gt; tenant
/// isolation -&gt; admin sees all sub-accounts + master summary -&gt; every step visible in
/// transactions/balance history/audit records -&gt; all five notification-worthy transitions reach
/// the stub receiver. One cohesive ordered walk (not independent Facts) because every step depends
/// on state built by the previous one, mirroring the real demo script's narrative. Split into small
/// per-step private helpers purely to satisfy the method-length analyzer rule — the steps still run
/// strictly in sequence against one shared <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public sealed class DemoScriptEndToEndTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private const string AdminClientCompanyId = "apiso-admin";

    private WebApplicationFactory<Program> WithMockMode() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MockMode:Enabled"] = "true",
                ["MockMode:WebhookDelayMilliseconds"] = "0",
            });
        });
        builder.ConfigureServices(services =>
        {
            // The background poller races the still-in-flight second SaveChangesAsync of the
            // request that scheduled the webhook (delay 0 makes it due immediately) — remove it
            // so this test drives every scheduled mock webhook deterministically via explicit
            // DispatchDueWebhooksAsync calls instead, exactly like MockWebhookDispatcher's own
            // doc comment describes for test-controlled delivery.
            var racyHostedServiceDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(MockWebhookDispatchBackgroundService))
                .ToList();
            foreach (var descriptor in racyHostedServiceDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    });

    [Fact]
    public async Task DemoScript_WalksEndToEnd_AcrossAllPhase1Slices()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = WithMockMode();
        using var adminClient = CreateClientFor(app, AdminClientCompanyId);

        var (clientCompanyIdA, subAccountIdA, circleWalletIdA) =
            await CreateAcceptedSubAccountAsync(app, adminClient, "Acme Inc", ct);
        await RejectAndResubmitSecondSubAccountAsync(app, adminClient, ct);

        using var clientA = CreateClientFor(app, clientCompanyIdA);
        await GenerateDepositAddressAsync(clientA, subAccountIdA, ct);
        var depositTransaction = await SimulateDepositAsync(app, clientA, subAccountIdA, circleWalletIdA, ct);
        var recipient = await RegisterAndApproveRecipientAsync(app, clientA, subAccountIdA, ct);
        var transfer = await CreateAndCompleteTransferAsync(app, clientA, subAccountIdA, recipient.Id, ct);
        var redemption = await CreateAndCompleteRedemptionAsync(app, clientA, subAccountIdA, ct);

        var clientCompanyIdC = await AssertTenantIsolationAsync(app, adminClient, clientA, ct);
        await AssertAdminVisibilityAsync(
            adminClient, clientCompanyIdA, clientCompanyIdC, depositTransaction.ProviderReferenceId, ct);
        await AssertTransactionAndBalanceHistoryAsync(app, clientA, clientCompanyIdA, subAccountIdA, ct);

        await AssertEventuallyDeliveredAsync(app, "EntityRegistrationDecided", subAccountIdA.ToString(), ct);
        await AssertEventuallyDeliveredAsync(
            app, "DepositCredited", depositTransaction.TransactionId.ToString(), ct);
        await AssertEventuallyDeliveredAsync(app, "RecipientApprovalDecided", recipient.Id.ToString(), ct);
        await AssertEventuallyDeliveredAsync(app, "TransferCompleted", transfer.Id.ToString(), ct);
        await AssertEventuallyDeliveredAsync(app, "RedemptionCompleted", redemption.Id.ToString(), ct);
    }

    private static async Task<(string ClientCompanyId, Guid SubAccountId, string CircleWalletId)> CreateAcceptedSubAccountAsync(
        WebApplicationFactory<Program> app, HttpClient adminClient, string businessName, CancellationToken ct)
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var response = await PostWithIdempotencyKeyAsync(
            adminClient, "v1/sub-accounts", ValidCreateRequest(clientCompanyId, businessName), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateSubAccountResponse>(ct);
        Assert.NotNull(created);

        await DispatchDueWebhooksAsync(app, ct);

        var getResponse = await adminClient.GetAsync($"v1/sub-accounts/{clientCompanyId}", ct);
        var subAccount = await getResponse.Content.ReadFromJsonAsync<SubAccountResponse>(ct);
        Assert.Equal("Active", subAccount!.LifecycleState);

        return (clientCompanyId, created!.SubAccountId, created.CircleWalletId);
    }

    private static async Task RejectAndResubmitSecondSubAccountAsync(
        WebApplicationFactory<Program> app, HttpClient adminClient, CancellationToken ct)
    {
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var createResponse = await PostWithIdempotencyKeyAsync(
            adminClient, "v1/sub-accounts", ValidCreateRequest(clientCompanyId, "Bad Co REJECTME"), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Drive the mock "externalEntities" decision webhook through the same real inbound
        // pipeline a live Circle SNS delivery uses.
        await DispatchDueWebhooksAsync(app, ct);

        var getAfterDecision = await adminClient.GetAsync($"v1/sub-accounts/{clientCompanyId}", ct);
        var subAccountAfterDecision = await getAfterDecision.Content.ReadFromJsonAsync<SubAccountResponse>(ct);
        Assert.Equal("Rejected", subAccountAfterDecision!.LifecycleState);

        using var subAccountClient = CreateClientFor(app, clientCompanyId);
        var resubmitResponse = await PostWithIdempotencyKeyAsync(
            subAccountClient,
            $"v1/sub-accounts/{clientCompanyId}/registrations",
            ValidResubmitRequest("Bad Co Corrected"),
            ct);
        Assert.Equal(HttpStatusCode.Created, resubmitResponse.StatusCode);
        var resubmitted = await resubmitResponse.Content.ReadFromJsonAsync<ResubmitEntityRegistrationResponse>(ct);
        Assert.Equal("PendingCompliance", resubmitted!.LifecycleState);

        await DispatchDueWebhooksAsync(app, ct);

        // Resubmission issues a fresh externalEntities wallet id at the provider;
        // ResubmitEntityRegistrationHandler resyncs SubAccount.CircleWalletId to it
        // (SubAccount.UpdateCircleWalletId) so the resubmission's own decision webhook can find
        // this row and transition it to Active.
        var getAfterResubmit = await adminClient.GetAsync($"v1/sub-accounts/{clientCompanyId}", ct);
        var subAccountAfterResubmit = await getAfterResubmit.Content.ReadFromJsonAsync<SubAccountResponse>(ct);
        Assert.Equal("Active", subAccountAfterResubmit!.LifecycleState);

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var resubmitAuditExists = await dbContext.AuditRecords.AnyAsync(
                r => r.ClientCompanyId == clientCompanyId
                    && r.EventType == "EntityRegistrationResubmitted",
                ct);
            Assert.True(resubmitAuditExists, "Expected an audit record for the resubmission.");
        }
    }

    private static async Task GenerateDepositAddressAsync(HttpClient clientA, Guid subAccountId, CancellationToken ct)
    {
        var response = await clientA.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/deposit-addresses",
            new GenerateDepositAddressRequest("ETH", "USDC"),
            ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<TransactionResponse> SimulateDepositAsync(
        WebApplicationFactory<Program> app,
        HttpClient clientA,
        Guid subAccountId,
        string circleWalletId,
        CancellationToken ct)
    {
        var depositId = $"deposit-{Guid.NewGuid():N}";
        using var webhookClient = app.CreateClient();
        var webhookResponse = await webhookClient.PostAsJsonAsync(
            "v1/webhooks/circle", DepositsSnsEnvelope(circleWalletId, depositId, "1000.00"), ct);
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);

        var transactionsResponse = await clientA.GetAsync($"v1/sub-accounts/{subAccountId}/transactions", ct);
        var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>(ct);
        var depositTransaction = Assert.Single(transactions!);
        Assert.Equal(TransactionType.Deposit, depositTransaction.Type);
        Assert.Equal(TransactionStatus.Complete, depositTransaction.Status);
        Assert.Equal(depositId, depositTransaction.ProviderReferenceId);

        var balanceResponse = await clientA.GetAsync($"v1/sub-accounts/{subAccountId}/balances", ct);
        var balance = await balanceResponse.Content.ReadFromJsonAsync<BalanceResponse>(ct);
        Assert.Equal(1000.00m, balance!.Balance.Amount);

        return depositTransaction;
    }

    private static async Task<RecipientResponse> RegisterAndApproveRecipientAsync(
        WebApplicationFactory<Program> app, HttpClient clientA, Guid subAccountId, CancellationToken ct)
    {
        var registerResponse = await clientA.PostAsJsonAsync(
            $"v1/sub-accounts/{subAccountId}/recipients",
            new RegisterRecipientRequest("ETH", "0xabc123recipient", "Vendor A"),
            ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var recipient = await registerResponse.Content.ReadFromJsonAsync<RecipientResponse>(ct);
        Assert.Equal(RecipientStatus.PendingApproval, recipient!.Status);

        await DispatchDueWebhooksAsync(app, ct);

        var getResponse = await clientA.GetAsync(
            $"v1/sub-accounts/{subAccountId}/recipients/{recipient.Id}", ct);
        var afterDecision = await getResponse.Content.ReadFromJsonAsync<RecipientResponse>(ct);
        Assert.Equal(RecipientStatus.Active, afterDecision!.Status);

        return afterDecision;
    }

    private static async Task<TransferResponse> CreateAndCompleteTransferAsync(
        WebApplicationFactory<Program> app, HttpClient clientA, Guid subAccountId, Guid recipientId, CancellationToken ct)
    {
        var createResponse = await PostWithIdempotencyKeyAsync(
            clientA,
            $"v1/sub-accounts/{subAccountId}/transfers",
            new CreateTransferRequest(recipientId, new Money(100m, "USDC")),
            ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var transfer = await createResponse.Content.ReadFromJsonAsync<TransferResponse>(ct);
        Assert.NotNull(transfer);

        await DispatchDueWebhooksAsync(app, ct);

        var getResponse = await clientA.GetAsync(
            $"v1/sub-accounts/{subAccountId}/transfers/{transfer!.Id}", ct);
        var afterCompletion = await getResponse.Content.ReadFromJsonAsync<TransferResponse>(ct);
        Assert.Equal(TransferStatus.Complete, afterCompletion!.Status);

        return afterCompletion;
    }

    private static async Task<RedemptionResponse> CreateAndCompleteRedemptionAsync(
        WebApplicationFactory<Program> app, HttpClient clientA, Guid subAccountId, CancellationToken ct)
    {
        var linkedBankAccountResponse = await PostWithIdempotencyKeyAsync(
            clientA,
            $"v1/sub-accounts/{subAccountId}/linked-bank-accounts",
            ValidLinkedBankAccountRequest(),
            ct);
        Assert.Equal(HttpStatusCode.Created, linkedBankAccountResponse.StatusCode);
        var linkedBankAccount = await linkedBankAccountResponse.Content
            .ReadFromJsonAsync<LinkedBankAccountResponse>(ct);
        Assert.NotNull(linkedBankAccount);

        await DispatchDueWebhooksAsync(app, ct);

        var getLinkedBankAccountResponse = await clientA.GetAsync(
            $"v1/sub-accounts/{subAccountId}/linked-bank-accounts/{linkedBankAccount!.Id}", ct);
        var linkedBankAccountAfterActivation = await getLinkedBankAccountResponse.Content
            .ReadFromJsonAsync<LinkedBankAccountResponse>(ct);
        Assert.Equal(LinkedBankAccountStatus.Active, linkedBankAccountAfterActivation!.Status);

        var createRedemptionResponse = await PostWithIdempotencyKeyAsync(
            clientA,
            $"v1/sub-accounts/{subAccountId}/redemptions",
            new CreateRedemptionRequest(linkedBankAccount.Id, new Money(50m, "USDC")),
            ct);
        Assert.Equal(HttpStatusCode.Created, createRedemptionResponse.StatusCode);
        var redemption = await createRedemptionResponse.Content.ReadFromJsonAsync<RedemptionResponse>(ct);
        Assert.NotNull(redemption);

        await DispatchDueWebhooksAsync(app, ct);

        var getRedemptionResponse = await clientA.GetAsync(
            $"v1/sub-accounts/{subAccountId}/redemptions/{redemption!.Id}", ct);
        var redemptionAfterCompletion = await getRedemptionResponse.Content
            .ReadFromJsonAsync<RedemptionResponse>(ct);

        Assert.Equal(TransferStatus.Complete, redemptionAfterCompletion!.Status);
        Assert.Equal(50m, redemptionAfterCompletion.GrossAmount.Amount);
        Assert.NotNull(redemptionAfterCompletion.Fees);
        Assert.NotNull(redemptionAfterCompletion.NetAmount);
        Assert.Equal(0m, redemptionAfterCompletion.Fees!.Amount);
        Assert.Equal(50m, redemptionAfterCompletion.NetAmount!.Amount);

        return redemptionAfterCompletion;
    }

    private static async Task<string> AssertTenantIsolationAsync(
        WebApplicationFactory<Program> app, HttpClient adminClient, HttpClient clientA, CancellationToken ct)
    {
        var clientCompanyIdC = $"client-{Guid.NewGuid():N}";
        var createResponse = await PostWithIdempotencyKeyAsync(
            adminClient, "v1/sub-accounts", ValidCreateRequest(clientCompanyIdC, "Third Co"), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSubAccountResponse>(ct);
        Assert.NotNull(created);
        await DispatchDueWebhooksAsync(app, ct);

        using var webhookClient = app.CreateClient();
        await webhookClient.PostAsJsonAsync(
            "v1/webhooks/circle",
            DepositsSnsEnvelope(created!.CircleWalletId, $"deposit-{Guid.NewGuid():N}", "50.00"),
            ct);

        var crossTenantTransactions = await clientA.GetAsync(
            $"v1/sub-accounts/{created.SubAccountId}/transactions", ct);
        var crossTenantTransactionsList = await crossTenantTransactions.Content
            .ReadFromJsonAsync<List<TransactionResponse>>(ct);
        Assert.True(crossTenantTransactionsList is null || crossTenantTransactionsList.Count == 0);

        var crossTenantSubAccount = await clientA.GetAsync($"v1/sub-accounts/{clientCompanyIdC}", ct);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantSubAccount.StatusCode);
        Assert.Equal(
            "application/problem+json", crossTenantSubAccount.Content.Headers.ContentType?.MediaType);

        return clientCompanyIdC;
    }

    private static async Task AssertAdminVisibilityAsync(
        HttpClient adminClient,
        string clientCompanyIdA,
        string clientCompanyIdC,
        string depositProviderReferenceId,
        CancellationToken ct)
    {
        var listAllSubAccountsResponse = await adminClient.GetAsync("v1/sub-accounts", ct);
        Assert.Equal(HttpStatusCode.OK, listAllSubAccountsResponse.StatusCode);
        var allSubAccounts = await listAllSubAccountsResponse.Content
            .ReadFromJsonAsync<List<SubAccountResponse>>(ct);
        Assert.NotNull(allSubAccounts);
        Assert.Contains(
            allSubAccounts!, s => string.Equals(s.ClientCompanyId, clientCompanyIdA, StringComparison.Ordinal));
        Assert.Contains(
            allSubAccounts!, s => string.Equals(s.ClientCompanyId, clientCompanyIdC, StringComparison.Ordinal));

        var adminTransactionsResponse = await adminClient.GetAsync(
            $"v1/admin/transactions?clientCompanyId={clientCompanyIdA}", ct);
        Assert.Equal(HttpStatusCode.OK, adminTransactionsResponse.StatusCode);
        var adminTransactions = await adminTransactionsResponse.Content
            .ReadFromJsonAsync<List<AdminTransactionResponse>>(ct);
        Assert.NotNull(adminTransactions);
        Assert.Contains(
            adminTransactions!,
            t => t.Type == TransactionType.Deposit
                && string.Equals(t.ProviderReferenceId, depositProviderReferenceId, StringComparison.Ordinal));

        var masterSummaryResponse = await adminClient.GetAsync("v1/admin/master-account/summary", ct);
        Assert.Equal(HttpStatusCode.OK, masterSummaryResponse.StatusCode);
        var masterSummary = await masterSummaryResponse.Content
            .ReadFromJsonAsync<GetMasterAccountSummaryResult>(ct);
        Assert.NotNull(masterSummary);
        Assert.Equal("USDC", masterSummary!.MainWalletBalance.CurrencyCode);
        Assert.True(masterSummary.SubAccountCount >= 3);
    }

    private static async Task AssertTransactionAndBalanceHistoryAsync(
        WebApplicationFactory<Program> app,
        HttpClient clientA,
        string clientCompanyIdA,
        Guid subAccountId,
        CancellationToken ct)
    {
        var finalTransactionsResponse = await clientA.GetAsync($"v1/sub-accounts/{subAccountId}/transactions", ct);
        var finalTransactions = await finalTransactionsResponse.Content
            .ReadFromJsonAsync<List<TransactionResponse>>(ct);
        Assert.NotNull(finalTransactions);
        Assert.Contains(finalTransactions!, t => t.Type == TransactionType.Deposit);
        Assert.Contains(finalTransactions!, t => t.Type == TransactionType.Transfer);
        Assert.Contains(finalTransactions!, t => t.Type == TransactionType.Redemption);

        var finalBalanceResponse = await clientA.GetAsync($"v1/sub-accounts/{subAccountId}/balances", ct);
        var finalBalance = await finalBalanceResponse.Content.ReadFromJsonAsync<BalanceResponse>(ct);
        Assert.NotNull(finalBalance);
        // 1000 deposit - 100 transfer - 50 redemption gross debit.
        Assert.Equal(850m, finalBalance!.Balance.Amount);

        var balanceHistoryResponse = await clientA.GetAsync($"v1/sub-accounts/{subAccountId}/balances/history", ct);
        var balanceHistory = await balanceHistoryResponse.Content
            .ReadFromJsonAsync<List<BalanceSnapshotResponse>>(ct);
        Assert.NotNull(balanceHistory);
        Assert.True(
            balanceHistory!.Count >= 3, "Expected a snapshot each for deposit, transfer debit and redemption debit.");

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
        var auditRecordExists = await dbContext.AuditRecords.AnyAsync(
            r => r.ClientCompanyId == clientCompanyIdA
                && r.EventType == "SubAccountRequested",
            ct);
        Assert.True(auditRecordExists, "Expected an audit record for SubAccount A's creation.");
    }

    private static HttpClient CreateClientFor(WebApplicationFactory<Program> app, string clientCompanyId)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("ClientCompanyId", clientCompanyId);
        return client;
    }

    private static async Task DispatchDueWebhooksAsync(WebApplicationFactory<Program> app, CancellationToken ct)
    {
        using var scope = app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<MockWebhookDispatcher>();
        await dispatcher.DispatchDueAsync(ct);
    }

    private static async Task<HttpResponseMessage> PostWithIdempotencyKeyAsync<TRequest>(
        HttpClient client, string requestUri, TRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
        return await client.SendAsync(message, ct);
    }

    private static async Task AssertEventuallyDeliveredAsync(
        WebApplicationFactory<Program> app, string eventType, string entityId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var entry = await dbContext.NotificationOutboxEntries
                .Where(e => e.EventType == eventType && e.EntityId == entityId)
                .SingleAsync(ct);

            if (entry.Status == NotificationDeliveryStatus.Delivered)
            {
                return;
            }

            await Task.Delay(200, ct);
        }

        Assert.Fail($"Outbox entry for '{eventType}'/'{entityId}' was not delivered within the deadline.");
    }

    private static CreateSubAccountRequest ValidCreateRequest(string clientCompanyId, string businessName) => new(
        ClientCompanyId: clientCompanyId,
        BusinessName: businessName,
        BusinessUniqueIdentifier: $"EIN-{Guid.NewGuid():N}"[..20],
        IdentifierIssuingCountryCode: "US",
        Country: "US",
        State: "NY",
        City: "New York",
        Postcode: "10001",
        StreetName: "Broadway",
        BuildingNumber: "1");

    private static ResubmitEntityRegistrationRequest ValidResubmitRequest(string businessName) => new(
        BusinessName: businessName,
        BusinessUniqueIdentifier: $"EIN-{Guid.NewGuid():N}"[..20],
        IdentifierIssuingCountryCode: "US",
        Country: "US",
        State: "NY",
        City: "New York",
        Postcode: "10002",
        StreetName: "Fifth Avenue",
        BuildingNumber: "5");

    private static CreateLinkedBankAccountRequest ValidLinkedBankAccountRequest() => new(
        BeneficiaryName: "Acme Inc",
        AccountNumber: "000123456789",
        RoutingNumber: "021000021",
        BankName: "Mock Bank",
        BillingName: "Acme Inc",
        BillingCity: "New York",
        BillingCountry: "US",
        BillingLine1: "1 Broadway",
        BillingPostalCode: "10001",
        BillingLine2: null,
        BillingDistrict: null,
        BankAddressCountry: "US",
        BankAddressBankName: "Mock Bank");

    private static object DepositsSnsEnvelope(string walletId, string depositId, string amount)
    {
        var innerMessage = JsonSerializer.Serialize(new
        {
            notificationType = "deposits",
            deposit = new
            {
                id = depositId,
                walletId,
                amount = new { amount, currency = "USD" },
            },
        });

        return new
        {
            Type = "Notification",
            MessageId = $"msg-{Guid.NewGuid():N}",
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test-topic",
            Message = innerMessage,
            Signature = "irrelevant-under-mock-verifier",
            SigningCertURL = "https://sns.us-east-1.amazonaws.com/cert.pem",
        };
    }
}
