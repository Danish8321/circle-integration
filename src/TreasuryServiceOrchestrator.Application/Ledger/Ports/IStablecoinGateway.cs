using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

/// <summary>
/// All money-moving provider ops (deposit addresses, redemptions, transfers, recipients, main
/// wallet balance) — owned by the Ledger module, per ADR 0006. Deliberately empty as of ticket
/// 02: ticket 01 shipped no Ledger money-moving use cases yet, so there is nothing to mock.
/// Tickets 03/05/06/07 each extend this interface with their own new methods as those slices
/// land; <c>MockStablecoinGateway</c>/<c>CircleMintGateway</c> grow alongside it.
/// </summary>
public interface IStablecoinGateway
{
    Task<GeneratedDepositAddress> GenerateDepositAddressAsync(
        GenerateDepositAddressGatewayRequest request, CancellationToken ct = default);

    Task<RegisteredRecipient> RegisterRecipientAsync(
        RegisterRecipientGatewayRequest request, CancellationToken ct = default);

    Task<CreatedTransfer> CreateTransferAsync(
        CreateTransferGatewayRequest request, CancellationToken ct = default);

    Task<CreatedRedeem> RedeemAsync(
        RedeemGatewayRequest request, CancellationToken ct = default);

    Task<CreatedLinkedBankAccount> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken ct = default);

    Task<WireInstructions> GetWireInstructionsAsync(
        string circleBankAccountId, CancellationToken ct = default);

    // Ticket 08.4 — Master Account (Distributor) main-wallet balance, consumed only by
    // GetMasterAccountSummaryQueryHandler (Admin module). walletId is deliberately omitted on
    // the CircleMintGateway implementation's GET /v1/businessAccount/balances call — the one
    // exception to the walletId-must-be-explicit rule elsewhere in this gateway, see
    // docs/features/12-admin-cross-tenant-views.md §3.
    Task<Money> GetMainWalletBalanceAsync(CancellationToken ct = default);

    // Ticket 15 — reconciliation. Merges the two Circle listing calls (wire deposits + on-chain
    // transfers into this wallet) into one result, per
    // docs/features/05-reliability-and-error-handling.md §7.2-7.3.
    Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken ct = default);
}
