namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <summary>
/// Master Account summary is not tenant-keyed at all — there is no ClientCompanyId to resolve
/// against (docs/features/12-admin-cross-tenant-views.md §1). The Admin gate lives entirely in
/// MasterAccountController, not here.
/// </summary>
public sealed record GetMasterAccountSummaryQuery;
