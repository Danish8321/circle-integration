using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Ledger.LinkedBankAccounts;

namespace TreasuryServiceOrchestrator.Api.Ledger;

[ApiController]
[Route("v1/sub-accounts/{subAccountId:guid}/linked-bank-accounts")]
public sealed class CreateLinkedBankAccountController(
    CreateLinkedBankAccountCommandHandler createLinkedBankAccountHandler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<LinkedBankAccountResponse>> CreateLinkedBankAccount(
        Guid subAccountId,
        [FromBody] CreateLinkedBankAccountRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Caller-supplied idempotency key (invariant 11), forwarded verbatim to Circle's
        // wire-creation call — same header convention as CreateTransferController.
        var result = await createLinkedBankAccountHandler.HandleAsync(
            new CreateLinkedBankAccountCommand(
                subAccountId,
                request.BeneficiaryName,
                request.AccountNumber,
                request.RoutingNumber,
                request.BankName,
                request.BillingName,
                request.BillingCity,
                request.BillingCountry,
                request.BillingLine1,
                request.BillingPostalCode,
                request.BillingLine2,
                request.BillingDistrict,
                request.BankAddressCountry,
                request.BankAddressBankName,
                idempotencyKey),
            cancellationToken);

        return CreatedAtAction(
            nameof(LinkedBankAccountDetailController.GetLinkedBankAccount),
            "LinkedBankAccountDetail",
            new { subAccountId, linkedBankAccountId = result.Id },
            LinkedBankAccountResponse.Map(result));
    }
}
