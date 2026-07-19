using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Compliance.Ports;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Abstractions;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;

public sealed class CreateSubAccountHandler(
    ISubAccountGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IValidator<CreateSubAccountCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<CreateSubAccountCommand, CreateSubAccountResult>
{
    public async Task<CreateSubAccountResult> HandleAsync(
        CreateSubAccountCommand command, CancellationToken cancellationToken = default)
    {
        // Sub-account creation is Admin-only regardless of the requested target tenant.
        if (!callerContext.IsAdmin)
        {
            throw new TenantForbiddenException();
        }

        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        return await IdempotencyExecutor.ExecuteAsync(
            idempotency,
            command.ClientCompanyId,
            command.IdempotencyKey,
            command,
            unitOfWork,
            () => ProvisionAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<CreateSubAccountResult> ProvisionAsync(
        CreateSubAccountCommand command, CancellationToken cancellationToken)
    {
        if (await subAccounts.GetByClientCompanyIdAsync(command.ClientCompanyId, cancellationToken) is not null)
        {
            throw new SubAccountAlreadyExistsException(command.ClientCompanyId);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Reserve: persist local intent before the outbound provider call.
        var subAccount = SubAccount.Create(command.ClientCompanyId, nowUtc);
        await subAccounts.AddAsync(subAccount, cancellationToken);

        await auditLog.AppendAsync(
            "SubAccountRequested", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(command), command.ClientCompanyId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Gateway/state-transition: call the provider.
        var gatewayResult = await gateway.CreateExternalEntityAsync(
            new CreateExternalEntityGatewayRequest(
                BusinessName: command.BusinessName,
                BusinessUniqueIdentifier: command.BusinessUniqueIdentifier,
                IdentifierIssuingCountryCode: command.IdentifierIssuingCountryCode,
                Country: command.Country,
                State: command.State,
                City: command.City,
                Postcode: command.Postcode,
                StreetName: command.StreetName,
                BuildingNumber: command.BuildingNumber),
            cancellationToken);

        subAccount.BeginCompliance(gatewayResult.WalletId);

        var registrationStatus = EntityRegistrationStatusMapper.Map(gatewayResult.ComplianceState);
        var registration = EntityRegistration.Create(
            subAccount.Id,
            command.ClientCompanyId,
            command.BusinessName,
            command.BusinessUniqueIdentifier,
            command.IdentifierIssuingCountryCode,
            command.Country,
            command.State,
            command.City,
            command.Postcode,
            command.StreetName,
            command.BuildingNumber,
            gatewayResult.WalletId,
            nowUtc);

        // Complete: finalize local state to match the provider's response.
        await entityRegistrations.AddAsync(registration, cancellationToken);

        await auditLog.AppendAsync(
            "SubAccountProvisionedAtCircle", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { subAccount.CircleWalletId, registrationStatus }),
            command.ClientCompanyId, command.CorrelationId, cancellationToken);

        return new CreateSubAccountResult(
            subAccount.Id, subAccount.ClientCompanyId, subAccount.CircleWalletId!, subAccount.LifecycleState);
    }
}
