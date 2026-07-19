using FluentValidation;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class GenerateDepositAddressCommandHandler(
    IDepositAddressRepository depositAddresses,
    IStablecoinGateway gateway,
    IIdempotencyService idempotency,
    IUnitOfWork unitOfWork,
    IValidator<GenerateDepositAddressCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<GenerateDepositAddressCommand, GenerateDepositAddressResult>
{
    public async Task<GenerateDepositAddressResult> HandleAsync(
        GenerateDepositAddressCommand command, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var idempotencyKey = $"deposit-address:{command.SubAccountId}:{command.Chain}:{command.Currency}";

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            callerContext.CallerId,
            idempotencyKey,
            command,
            unitOfWork,
            () => GenerateAsync(command, idempotencyKey, cancellationToken),
            cancellationToken);
    }

    private async Task<GenerateDepositAddressResult> GenerateAsync(
        GenerateDepositAddressCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Local dedup on (SubAccountId, Chain, Currency) is separate from the idempotency key:
        // reuse an existing address without calling the gateway.
        var existing = await depositAddresses.FindAsync(
            command.SubAccountId, command.Chain, command.Currency, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var gatewayResult = await gateway.GenerateDepositAddressAsync(
            new GenerateDepositAddressGatewayRequest(command.Chain, command.Currency, idempotencyKey),
            cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var depositAddress = DepositAddress.Create(
            command.SubAccountId,
            gatewayResult.Chain,
            gatewayResult.Currency,
            gatewayResult.Address,
            gatewayResult.ProviderAddressId,
            nowUtc);

        await depositAddresses.AddAsync(depositAddress, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(depositAddress);
    }

    private static GenerateDepositAddressResult Map(DepositAddress depositAddress) => new(
        depositAddress.Id,
        depositAddress.SubAccountId,
        depositAddress.Chain,
        depositAddress.Currency,
        depositAddress.Address,
        depositAddress.CreatedAtUtc);
}
