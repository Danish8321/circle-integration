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
}
