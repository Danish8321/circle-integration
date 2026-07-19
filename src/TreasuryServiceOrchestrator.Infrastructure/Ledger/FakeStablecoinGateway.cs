using TreasuryServiceOrchestrator.Application.Ledger.Ports;

namespace TreasuryServiceOrchestrator.Infrastructure.Ledger;

/// <summary>
/// Deterministic, no-network stand-in for <see cref="CircleMintGateway"/>, wired only in
/// Development alongside <see cref="FakeSubAccountGateway"/> so the money-moving slices run end to
/// end without a Circle sandbox account (audit F2, ticket 22). Pairing a fake sub-account gateway
/// with the real Circle mint gateway was incoherent — real mint/redeem/transfer calls would
/// reference sub-accounts Circle never saw. Both gateways fake together, or neither.
/// Distinct from the formal mock-provider system (Phase 1 Task 6, PRD §13) which this repo doesn't
/// have yet. Returned status literals are the raw provider strings the *StatusMapper types expect
/// (e.g. recipient "active", linked bank "complete") so a dev flow reaches usable states offline.
/// </summary>
public sealed class FakeStablecoinGateway : IStablecoinGateway
{
    public Task<GeneratedDepositAddress> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken ct = default) =>
        Task.FromResult(new GeneratedDepositAddress(
            Address: $"0xdev{Guid.NewGuid():N}",
            Chain: request.Chain,
            Currency: request.Currency,
            ProviderAddressId: $"dev-addr-{Guid.NewGuid():N}"));

    public Task<RegisteredRecipient> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken ct = default) =>
        Task.FromResult(new RegisteredRecipient(
            CircleRecipientId: $"dev-recipient-{Guid.NewGuid():N}",
            Status: "active"));

    public Task<CreatedTransfer> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CreatedTransfer(
            CircleTransferId: $"dev-transfer-{Guid.NewGuid():N}",
            Status: "pending"));

    public Task<CreatedRedeem> RedeemAsync(
        RedeemGatewayRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CreatedRedeem(
            CircleRedeemId: $"dev-redeem-{Guid.NewGuid():N}",
            Status: "pending"));

    public Task<CreatedLinkedBankAccount> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CreatedLinkedBankAccount(
            CircleBankAccountId: $"dev-bank-{Guid.NewGuid():N}",
            Status: "complete"));

    public Task<WireInstructions> GetWireInstructionsAsync(
        string circleBankAccountId, CancellationToken ct = default) =>
        Task.FromResult(new WireInstructions(
            TrackingRef: $"DEV-WIRE-{circleBankAccountId}",
            BeneficiaryName: "Dev Fake Beneficiary",
            BeneficiaryAddress: "1 Dev Street, Devtown",
            BankName: "Dev Fake Bank",
            SwiftCode: "DEVUS33",
            RoutingNumber: "000000000",
            MaskedAccountNumber: "****0000",
            Currency: "USD"));

    public Task<Money> GetMainWalletBalanceAsync(CancellationToken ct = default) =>
        Task.FromResult(new Money(1_000_000m, "USD"));

    public Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProviderDepositRecord>>([]);
}
