Status: open

Source: `docs/features/12-admin-cross-tenant-views.md` (old source `docs/Phase_1_Feature_Slices.md`
Task 12, deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 07-redemption-and-linked-bank-account.

## Scope

Three admin-only reads: current-balance summary column on the existing all-sub-accounts list,
all-tenants transaction view (`GET /transactions?clientCompanyId=...`, filter optional),
`GET /master-account/summary` (main-wallet balance + sum of every sub-account's latest balance
snapshot). Drill-down (one sub-account, admin names target) needs no new code —
`TenantScopeResolver.Resolve` already handles it (Tasks 1-2, already built).

**Explicitly out of scope** (per the doc's own scope note): `/master-account/deposits`,
`/master-account/wire-instructions`, `/master-account/bank-accounts` — no backing entity exists
for any of them and PRD §15.1 slice 8 only requires "master-account summary".
`/master-account/bank-accounts` is already covered by 07's `GET /api/v1/linked-bank-accounts`
(Admin-only) — do not add a second route for the same data.

## Files (see Task 12 for exact list — paths below corrected 2026-07-17 to match code as
shipped, not the Phase 1 plan's pre-B0.5 `SubAccounts/` namespace and result/handler names)

- Modify: `Application/Compliance/GetSubAccount/SubAccountDetailsResult.cs`,
  `Application/Compliance/ListSubAccounts/ListSubAccountsHandler.cs`,
  `Application/Ledger/Ports/ITransactionRepository.cs`, `TransactionRepository`, `GatewayDtos`,
  `IStablecoinGateway`, `CircleMintGateway`, `MockProviderOptions`, `MockStablecoinGateway`.
- New: `Application/Ledger/ListAllTransactionsQuery.cs`,
  `Application/Admin/{GetMasterAccountSummaryQuery,GetMasterAccountSummaryQueryHandler}.cs`,
  `Api/Admin/{AdminTransactionsController,MasterAccountController}.cs` (module-scoped path,
  matching the existing `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`).
- Modify: `TransactionsController` (created by ticket 04, exists by the time this ticket
  starts), `Program.cs`.

## Key corrections that apply

- **Design-pass #4**: `ITransactionRepository.ListAllAsync` takes
  `TransactionListFilter(string? ClientCompanyId, TransactionType? Type, TransactionStatus?
  Status, DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize)`, not eight positional
  parameters.
- Design-pass #3: admin list/summary endpoints are the ones that actually match on
  `TenantScope` (`Single | AllTenants`) rather than a plain `ClientCompanyId`.

## Definition of done

- `ListSubAccountsQueryHandlerTests` updated for the new balance-summary column.
- `GetMasterAccountSummaryQueryHandlerTests` (Moq) — covers main wallet + sum of latest
  snapshots across all sub-accounts.
- Api integration tests for `AdminTransactionsController`, `MasterAccountController` — assert
  non-admin callers are rejected structurally, not just by a conditional.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run.

## Comments
