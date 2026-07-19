using System.Globalization;
using System.Text.Json;
using TreasuryServiceOrchestrator.Application.Ledger.Redemptions;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Webhooks;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

/// <summary>
/// Ticket 07.5: processes the real Circle "payouts" SNS topic
/// (docs/features/11-redemption-and-payouts.md §7). <c>id</c>/<c>status</c>/<c>amount</c>/
/// <c>fees</c>/<c>currency</c> are required — missing any throws
/// <see cref="InvalidOperationException"/>. <c>toAmount</c> is intentionally excluded from that
/// check (correction #3): its absence must not throw. This is the one place the
/// optional-<c>toAmount</c>-vs-computed-fallback branch is resolved — <c>netAmount = toAmount</c>
/// when present, else <c>amount - fees</c> — before forwarding the already-resolved
/// <c>NetAmount</c>/<c>Fees</c> into <see cref="ProcessPayoutStatusCommand"/>.
/// </summary>
public sealed class PayoutsWebhookTopicProcessor(
    ICommandHandler<ProcessPayoutStatusCommand, ProcessPayoutStatusResult> processPayoutStatusHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "payouts";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<PayoutsWebhookEnvelope>(payloadJson)
            ?? throw new InvalidOperationException("Empty payouts webhook payload.");

        var payout = envelope.Payout
            ?? throw new InvalidOperationException("payouts webhook payload missing 'payout'.");

        if (string.IsNullOrWhiteSpace(payout.Id)
            || string.IsNullOrWhiteSpace(payout.Status)
            || string.IsNullOrWhiteSpace(payout.Amount)
            || string.IsNullOrWhiteSpace(payout.Fees)
            || string.IsNullOrWhiteSpace(payout.Currency))
        {
            throw new InvalidOperationException(
                "payouts webhook payload missing 'id', 'status', 'amount', 'fees', or 'currency'.");
        }

        var amount = decimal.Parse(payout.Amount, CultureInfo.InvariantCulture);
        var fees = decimal.Parse(payout.Fees, CultureInfo.InvariantCulture);
        var netAmountValue = payout.ToAmount is null
            ? amount - fees
            : decimal.Parse(payout.ToAmount, CultureInfo.InvariantCulture);

        var command = new ProcessPayoutStatusCommand(
            payout.Id,
            payout.Status,
            new Money(fees, payout.Currency),
            new Money(netAmountValue, payout.Currency));

        await processPayoutStatusHandler.HandleAsync(command, cancellationToken);
    }
}
