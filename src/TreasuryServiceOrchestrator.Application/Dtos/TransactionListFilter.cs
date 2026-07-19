using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

/// <summary>
/// Admin-only, all-tenant filter for <see cref="ListAllTransactionsQuery"/>. ClientCompanyId is
/// an optional filter field here, not a tenant scope — unlike every tenant-scoped query in this
/// codebase, this filter has no required ClientCompanyId, since the caller-side Admin gate is
/// enforced by AdminTransactionsController (08.3), not this filter or its repository query.
/// </summary>
public sealed record TransactionListFilter(
    string? ClientCompanyId = null,
    TransactionType? Type = null,
    TransactionStatus? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 20);
