using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Application.Compliance.CreateSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.GetSubAccount;
using TreasuryServiceOrchestrator.Application.Compliance.ListSubAccounts;
using TreasuryServiceOrchestrator.Application.Compliance.ResubmitEntityRegistration;
using TreasuryServiceOrchestrator.Application.Compliance.SetSubAccountDisabled;
using TreasuryServiceOrchestrator.Application.Shared;
using TreasuryServiceOrchestrator.Application.Shared.Ports;
using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Api.Compliance;

[ApiController]
[Route("v1/sub-accounts")]
public sealed class SubAccountsController(
    CreateSubAccountHandler createSubAccountHandler,
    GetSubAccountHandler getSubAccountHandler,
    ListSubAccountsHandler listSubAccountsHandler,
    SetSubAccountDisabledHandler setSubAccountDisabledHandler,
    ResubmitEntityRegistrationHandler resubmitEntityRegistrationHandler,
    ICallerContext callerContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SubAccountResponse>>> ListSubAccounts(
        [FromQuery] string? state, CancellationToken cancellationToken)
    {
        SubAccountLifecycleState? lifecycleState = null;
        if (state is not null)
        {
            if (!Enum.TryParse<SubAccountLifecycleState>(state, ignoreCase: true, out var parsed))
            {
                throw new FluentValidation.ValidationException($"Unknown lifecycle state '{state}'.");
            }

            lifecycleState = parsed;
        }

        // No requested tenant: Admin resolves to AllTenants; a SubAccount caller
        // resolves to SingleTenant, which the handler rejects (403 centrally).
        var scope = TenantScopeResolver.Resolve(callerContext, null);

        var results = await listSubAccountsHandler.HandleAsync(
            new ListSubAccountsQuery(scope, lifecycleState, HttpContext.TraceIdentifier),
            cancellationToken);

        return Ok(results.Select(result => new SubAccountResponse(
            result.SubAccountId,
            result.ClientCompanyId,
            result.LifecycleState,
            result.IsDisabled,
            result.CircleWalletId,
            result.LatestRegistrationStatus,
            result.RegistrationRejectionReason)).ToList());
    }

    [HttpGet("{clientCompanyId}")]
    public async Task<ActionResult<SubAccountResponse>> GetSubAccount(
        string clientCompanyId, CancellationToken cancellationToken)
    {
        // The route segment is always non-empty, so the resolved scope is always
        // SingleTenant (or TenantForbiddenException -> 403 centrally).
        var scope = (TenantScope.SingleTenant)TenantScopeResolver.Resolve(callerContext, clientCompanyId);

        var result = await getSubAccountHandler.HandleAsync(
            new GetSubAccountQuery(scope.ClientCompanyId), cancellationToken);

        return Ok(new SubAccountResponse(
            result.SubAccountId,
            result.ClientCompanyId,
            result.LifecycleState,
            result.IsDisabled,
            result.CircleWalletId,
            result.LatestRegistrationStatus,
            result.RegistrationRejectionReason));
    }

    [HttpPost]
    public async Task<ActionResult<CreateSubAccountResponse>> CreateSubAccount(
        [FromBody] CreateSubAccountRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Request validation guarantees a non-empty ClientCompanyId, so the resolved
        // scope is always SingleTenant (or TenantForbiddenException -> 403 centrally).
        var scope = (TenantScope.SingleTenant)TenantScopeResolver.Resolve(
            callerContext, request.ClientCompanyId);

        var correlationId = HttpContext.TraceIdentifier;

        var result = await createSubAccountHandler.HandleAsync(
            new CreateSubAccountCommand(
                scope.ClientCompanyId,
                request.BusinessName,
                request.BusinessUniqueIdentifier,
                request.IdentifierIssuingCountryCode,
                request.Country,
                request.State,
                request.City,
                request.Postcode,
                request.StreetName,
                request.BuildingNumber,
                idempotencyKey,
                correlationId),
            cancellationToken);

        return CreatedAtAction(
            nameof(CreateSubAccount),
            new { result.SubAccountId },
            new CreateSubAccountResponse(
                result.SubAccountId, result.ClientCompanyId, result.CircleWalletId, result.LifecycleState.ToString()));
    }

    [HttpPost("{clientCompanyId}/registrations")]
    public async Task<ActionResult<ResubmitEntityRegistrationResponse>> ResubmitEntityRegistration(
        string clientCompanyId,
        [FromBody] ResubmitEntityRegistrationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The route segment is always non-empty, so the resolved scope is always
        // SingleTenant (or TenantForbiddenException -> 403 centrally).
        var scope = (TenantScope.SingleTenant)TenantScopeResolver.Resolve(callerContext, clientCompanyId);

        var result = await resubmitEntityRegistrationHandler.HandleAsync(
            new ResubmitEntityRegistrationCommand(
                scope.ClientCompanyId,
                request.BusinessName,
                request.BusinessUniqueIdentifier,
                request.IdentifierIssuingCountryCode,
                request.Country,
                request.State,
                request.City,
                request.Postcode,
                request.StreetName,
                request.BuildingNumber,
                idempotencyKey,
                HttpContext.TraceIdentifier),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetSubAccount),
            new { clientCompanyId = result.ClientCompanyId },
            new ResubmitEntityRegistrationResponse(
                result.SubAccountId,
                result.ClientCompanyId,
                result.RegistrationId,
                result.LifecycleState,
                result.RegistrationStatus));
    }

    [HttpPut("{clientCompanyId}/disabled")]
    public async Task<ActionResult<SetSubAccountDisabledResponse>> SetSubAccountDisabled(
        string clientCompanyId,
        [FromBody] SetSubAccountDisabledRequest request,
        CancellationToken cancellationToken)
    {
        // The route segment is always non-empty, so the resolved scope is always
        // SingleTenant (or TenantForbiddenException -> 403 centrally).
        var scope = (TenantScope.SingleTenant)TenantScopeResolver.Resolve(callerContext, clientCompanyId);

        var result = await setSubAccountDisabledHandler.HandleAsync(
            new SetSubAccountDisabledCommand(
                scope.ClientCompanyId, request.Disabled, HttpContext.TraceIdentifier),
            cancellationToken);

        return Ok(new SetSubAccountDisabledResponse(
            result.SubAccountId, result.ClientCompanyId, result.IsDisabled));
    }
}
