using System.Text.Json;
using FluentValidation;
using TreasuryServiceOrchestrator.Application.Exceptions;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Handlers;

public sealed class ResubmitEntityRegistrationHandler(
    ISubAccountGateway gateway,
    IIdempotencyService idempotency,
    IAuditLogService auditLog,
    IUnitOfWork unitOfWork,
    ISubAccountRepository subAccounts,
    IEntityRegistrationRepository entityRegistrations,
    IValidator<ResubmitEntityRegistrationCommand> validator,
    TimeProvider timeProvider,
    ICallerContext callerContext)
    : ICommandHandler<ResubmitEntityRegistrationCommand, ResubmitEntityRegistrationResult>
{
    public async Task<ResubmitEntityRegistrationResult> HandleAsync(
        ResubmitEntityRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        // No admin-only guard: a SubAccount resubmits its own registration; the controller's
        // scope resolver enforces tenant, and Admin may act for a named tenant.
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
            () => ResubmitAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<ResubmitEntityRegistrationResult> ResubmitAsync(
        ResubmitEntityRegistrationCommand command, CancellationToken cancellationToken)
    {
        var subAccount = await subAccounts.GetByClientCompanyIdAsync(command.ClientCompanyId, cancellationToken)
            ?? throw new NotFoundException($"No sub-account for client company '{command.ClientCompanyId}'.");

        if (subAccount.LifecycleState != SubAccountLifecycleState.Rejected)
        {
            throw new ConflictException(
                $"Sub-account is {subAccount.LifecycleState}; only a Rejected registration may be resubmitted.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Reserve: transition local state back to pending before the outbound provider call.
        subAccount.ResubmitCompliance();

        await auditLog.AppendAsync(
            "EntityRegistrationResubmitted", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(command), callerContext.CallerId, command.CorrelationId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Gateway/state-transition: resubmit the corrected entity details to the provider.
        var gatewayResult = await gateway.CreateExternalEntityAsync(
            BuildGatewayRequest(command), cancellationToken);

        subAccount.UpdateCircleWalletId(gatewayResult.WalletId);

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

        // Complete: record the fresh registration reflecting the provider's response.
        await entityRegistrations.AddAsync(registration, cancellationToken);

        await auditLog.AppendAsync(
            "EntityRegistrationResubmitCompleted", "SubAccount", subAccount.Id.ToString(),
            JsonSerializer.Serialize(new { registration.Id, registrationStatus }),
            callerContext.CallerId, command.CorrelationId, cancellationToken);

        return new ResubmitEntityRegistrationResult(
            subAccount.Id,
            subAccount.ClientCompanyId,
            registration.Id,
            subAccount.LifecycleState.ToString(),
            registrationStatus.ToString());
    }

    private static CreateExternalEntityGatewayRequest BuildGatewayRequest(
        ResubmitEntityRegistrationCommand command) =>
        new(
            BusinessName: command.BusinessName,
            BusinessUniqueIdentifier: command.BusinessUniqueIdentifier,
            IdentifierIssuingCountryCode: command.IdentifierIssuingCountryCode,
            Country: command.Country,
            State: command.State,
            City: command.City,
            Postcode: command.Postcode,
            StreetName: command.StreetName,
            BuildingNumber: command.BuildingNumber);
}
