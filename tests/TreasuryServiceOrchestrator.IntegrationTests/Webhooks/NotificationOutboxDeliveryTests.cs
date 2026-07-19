using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TreasuryServiceOrchestrator.Application.Compliance.ProcessExternalEntityDecision;
using TreasuryServiceOrchestrator.Application.Ledger;
using TreasuryServiceOrchestrator.Application.Ledger.Recipients;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Ledger.Transfers;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.IntegrationTests.Webhooks;

/// <summary>
/// Ticket 09.6: proves each of the five notification-worthy transitions (spec §5's table)
/// actually reaches the internal-notifications stub receiver end to end — real DbContext/SQL
/// Server (Testcontainers), real <see cref="NotificationDispatchBackgroundService"/> polling
/// loop, real HTTP delivery (rewired to TestServer in-process by
/// <see cref="TreasuryServiceOrchestratorApiFactory"/>) against
/// <see cref="TreasuryServiceOrchestrator.Api.Webhooks.InternalNotificationsStubController"/>.
/// Not a mock-port assertion (that is ticket 09.5's job) — this is the outbox row's Status
/// actually flipping to Delivered.
/// </summary>
public sealed class NotificationOutboxDeliveryTests(TreasuryServiceOrchestratorApiFactory factory)
    : IClassFixture<TreasuryServiceOrchestratorApiFactory>
{
    private static async Task AssertEventuallyDeliveredAsync(
        IServiceProvider services, string eventType, string entityId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var entry = await dbContext.NotificationOutboxEntries
                .Where(e => e.EventType == eventType && e.EntityId == entityId)
                .SingleAsync(cancellationToken);

            if (entry.Status == NotificationDeliveryStatus.Delivered)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        Assert.Fail($"Outbox entry for '{eventType}'/'{entityId}' was not delivered within the deadline.");
    }

    [Fact]
    public async Task DepositCredited_IsEventuallyDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        Guid subAccountId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
            dbContext.SubAccounts.Add(subAccount);
            subAccountId = subAccount.Id;
            await dbContext.SaveChangesAsync(ct);

            var callerContext = scope.ServiceProvider.GetRequiredService<ISettableCallerContext>();
            callerContext.Set(clientCompanyId, CallerRole.SubAccount);

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessDepositCommand, ProcessDepositResult>>();
            var command = new ProcessDepositCommand(
                subAccountId,
                new Money(100m, "USDC"),
                $"provider-ref-{Guid.NewGuid():N}",
                DepositSourceType.Wire,
                $"corr-{Guid.NewGuid():N}");

            var result = await handler.HandleAsync(command, ct);

            await AssertEventuallyDeliveredAsync(
                factory.Services, "DepositCredited", result.TransactionId.ToString(), ct);
        }
    }

    [Fact]
    public async Task TransferCompleted_IsEventuallyDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleTransferId = $"circle-transfer-{Guid.NewGuid():N}";
        Guid transferId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
            dbContext.SubAccounts.Add(subAccount);

            var recipient = Recipient.Create(
                subAccount.Id, clientCompanyId, "ETH", "0xabc", "label", "circle-recipient-1",
                RecipientStatus.Active, DateTime.UtcNow);
            dbContext.Recipients.Add(recipient);

            var transfer = Transfer.Create(
                subAccount.Id, clientCompanyId, recipient.Id, new Money(50m, "USDC"),
                $"corr-{Guid.NewGuid():N}", DateTime.UtcNow);
            transfer.SetProviderReference(circleTransferId, DateTime.UtcNow);
            dbContext.Transfers.Add(transfer);
            transferId = transfer.Id;

            await dbContext.SaveChangesAsync(ct);

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessTransferStatusCommand, ProcessTransferStatusResult>>();
            await handler.HandleAsync(new ProcessTransferStatusCommand(circleTransferId, "complete"), ct);

            await AssertEventuallyDeliveredAsync(
                factory.Services, "TransferCompleted", transferId.ToString(), ct);
        }
    }

    [Fact]
    public async Task RedemptionCompleted_IsEventuallyDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleRedeemId = $"circle-redeem-{Guid.NewGuid():N}";
        Guid redeemRequestId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
            dbContext.SubAccounts.Add(subAccount);

            var redeemRequest = RedeemRequest.Create(
                subAccount.Id, clientCompanyId, Guid.NewGuid(), new Money(75m, "USDC"),
                $"corr-{Guid.NewGuid():N}", DateTime.UtcNow);
            redeemRequest.SetProviderReference(circleRedeemId, DateTime.UtcNow);
            dbContext.RedeemRequests.Add(redeemRequest);
            redeemRequestId = redeemRequest.Id;

            await dbContext.SaveChangesAsync(ct);

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult>>();
            await handler.HandleAsync(
                new ProcessPayoutStatusCommand(
                    circleRedeemId, "complete", new Money(1m, "USDC"), new Money(74m, "USDC")),
                ct);

            await AssertEventuallyDeliveredAsync(
                factory.Services, "RedemptionCompleted", redeemRequestId.ToString(), ct);
        }
    }

    [Fact]
    public async Task EntityRegistrationDecided_IsEventuallyDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleWalletId = $"circle-wallet-{Guid.NewGuid():N}";
        Guid subAccountId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
            subAccount.BeginCompliance(circleWalletId);
            dbContext.SubAccounts.Add(subAccount);
            subAccountId = subAccount.Id;

            var registration = EntityRegistration.Create(
                subAccount.Id, clientCompanyId, "Acme Inc.", "12345", "US", "US", "CA", "SF",
                "94105", "Market St", "1", circleWalletId, DateTime.UtcNow);
            dbContext.EntityRegistrations.Add(registration);

            await dbContext.SaveChangesAsync(ct);

            var handler = scope.ServiceProvider.GetRequiredService<ProcessExternalEntityDecisionHandler>();
            await handler.HandleAsync(
                new ProcessExternalEntityDecisionCommand(circleWalletId, "ACCEPTED", $"corr-{Guid.NewGuid():N}"),
                ct);

            await AssertEventuallyDeliveredAsync(
                factory.Services, "EntityRegistrationDecided", subAccountId.ToString(), ct);
        }
    }

    [Fact]
    public async Task RecipientApprovalDecided_IsEventuallyDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientCompanyId = $"client-{Guid.NewGuid():N}";
        var circleRecipientId = $"circle-recipient-{Guid.NewGuid():N}";
        Guid recipientId;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TreasuryServiceOrchestratorDbContext>();
            var subAccount = SubAccount.Create(clientCompanyId, DateTime.UtcNow);
            dbContext.SubAccounts.Add(subAccount);

            var recipient = Recipient.Create(
                subAccount.Id, clientCompanyId, "ETH", "0xdef", "label", circleRecipientId,
                RecipientStatus.PendingApproval, DateTime.UtcNow);
            dbContext.Recipients.Add(recipient);
            recipientId = recipient.Id;

            await dbContext.SaveChangesAsync(ct);

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessRecipientDecisionCommand, ProcessRecipientDecisionResult>>();
            await handler.HandleAsync(new ProcessRecipientDecisionCommand(circleRecipientId, "active"), ct);

            await AssertEventuallyDeliveredAsync(
                factory.Services, "RecipientApprovalDecided", recipientId.ToString(), ct);
        }
    }
}
