using FluentValidation;
using TreasuryServiceOrchestrator.Application.Ledger.Ports;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;

namespace TreasuryServiceOrchestrator.Application.Ledger.Recipients;

public sealed class RegisterRecipientCommandHandler(
    IRecipientRepository recipients,
    IStablecoinGateway gateway,
    IIdempotencyService idempotency,
    IUnitOfWork unitOfWork,
    IValidator<RegisterRecipientCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<RegisterRecipientCommand, RegisterRecipientResult>
{
    public async Task<RegisterRecipientResult> HandleAsync(
        RegisterRecipientCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var idempotencyKey = $"recipient:{callerContext.CallerId}:{command.Chain}:{command.Address}";

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            callerContext.CallerId,
            idempotencyKey,
            command,
            unitOfWork,
            () => RegisterAsync(command, idempotencyKey, cancellationToken),
            cancellationToken);
    }

    private async Task<RegisterRecipientResult> RegisterAsync(
        RegisterRecipientCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        var gatewayResult = await gateway.RegisterRecipientAsync(
            new RegisterRecipientGatewayRequest(command.Chain, command.Address, command.Label, idempotencyKey),
            cancellationToken);

        var status = RecipientStatusMapper.Map(gatewayResult.Status);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var recipient = Recipient.Create(
            command.SubAccountId,
            callerContext.CallerId,
            command.Chain,
            command.Address,
            command.Label,
            gatewayResult.CircleRecipientId,
            status,
            nowUtc);

        await recipients.AddAsync(recipient, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(recipient);
    }

    private static RegisterRecipientResult Map(Recipient recipient) => new(
        recipient.Id,
        recipient.SubAccountId,
        recipient.Chain,
        recipient.Address,
        recipient.Label,
        recipient.CircleRecipientId,
        recipient.Status,
        recipient.CreatedAtUtc);
}
