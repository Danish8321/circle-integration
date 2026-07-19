Status: S1+S2 shipped (build clean, unit 404/404). S3 (Testcontainers) + S4 (docs) remain.

## Shipped (S1+S2)
- DbContext injects `ICallerContext`; global `HasQueryFilter(x => callerContext.IsAdmin ||
  x.ClientCompanyId == callerContext.CallerId)` on all 11 tenant-owned entities. Config helpers
  made instance; entity blocks split (`ConfigureCoreEntities`/`ConfigureInfraEntities`) for MA0051.
- Bypass (`.IgnoreQueryFilters()`) on system/discovery/background reads. The spec's "5 lookups"
  undercounted — the filter would have silently emptied background jobs (no ambient caller):
  - 5 discovery lookups: `Transaction.GetByProviderReferenceIdAsync`, `Transfer/Recipient/
    RedeemRequest/LinkedBankAccount.FindByCircle*IdAsync`.
  - `SubAccount.GetByCircleWalletIdAsync` (webhook/compliance discovery).
  - `FundAccount.ListAllAsync` (snapshot pass), `SubAccount.ListActiveWithWalletAsync`
    (reconciliation pass), `NotificationOutbox.GetDueBatchAsync` (dispatcher).
- `ScheduledBalanceSnapshotService` now injects `ISettableCallerContext` and `Set`s the fund
  account's tenant before its scoped `GetByClientCompanyIdAsync` read — mirrors the existing
  `DepositReconciliationService` pattern. (Reconciliation already Set per-item; snapshot did not.)
- `SubAccountRepository.ListAsync` left filtered on purpose: a tenant listing sub-accounts should
  see only its own; admin rides the IsAdmin bypass.

## Open
- S3 Testcontainers proof (below) — the EF filter is not exercisable at unit tier; F7 is NOT
  "done" until this runs green.
- S4 docs (audit F7 -> RESOLVED, INV7 gloss confirm).

---

Status: spec — S1+S2 done
Priority: P1 — closes audit finding F7 (ticket 22). Structural tenant isolation (INV7).
Type: refactor / security-hardening

Source: audit F7 (`22-architecture-audit-2026-07-19.md`). INV7 requires cross-tenant access be
"structurally impossible at the data-access layer". Today it is by-convention: every repo method
takes a `clientCompanyId` param the caller must remember to pass, and the 5 tenant-less lookups
(`GetByProviderReferenceIdAsync`, `FindByCircle{Transfer,Recipient,Redeem,BankAccount}IdAsync`)
run with no tenant predicate at all. No live breach (those are system-context only), but nothing
structurally stops a future tenant-facing handler from calling one and leaking.

## Current mechanics (verified 2026-07-19)

- `DbContext` ctor takes only `DbContextOptions`. No tenant injected, no `HasQueryFilter` anywhere
  (grep empty).
- Ambient tenant identity already exists: `ICallerContext` (Application port) — `CallerId`, `Role`
  (`SubAccount|Admin`), `IsAdmin`. Api's `HttpCallerContext` (scoped) implements it, populated by
  `CallerIdentityMiddleware`. Webhook/reconciliation set tenant via `ISettableCallerContext.Set`
  AFTER discovering it through the tenant-less lookups.
- For a regular tenant, identity == tenant: `CallerId` IS the `ClientCompanyId`. For Admin,
  identity != tenant (target is request-derived, may be all tenants — `TenantScopeResolver`).
- 11 tenant-owned entities carry `ClientCompanyId`: AuditRecord, BalanceSnapshot,
  EntityRegistration, FundAccount, LinkedBankAccount, NotificationOutboxEntry, Recipient,
  RedeemRequest, SubAccount, Transaction, Transfer.

## Target shape

Global EF `HasQueryFilter` on the 11 entities, sourced from the ambient `ICallerContext` the
`DbContext` is given at construction:

```
e => callerContext.IsAdmin || e.ClientCompanyId == callerContext.CallerId
```

- **Regular tenant (SubAccount role):** restricted to own rows by construction. Forgetting the
  explicit `.Where` predicate, or calling a tenant-less lookup, can no longer leak. ✓ (the INV7 ask)
- **Admin:** filter is bypassed in the predicate itself; admin scoping still enforced by the
  explicit predicate / filter param in `ListAllAsync` + INV8 audit. Admin is already privileged
  and audited, so the bypass is not a new trust grant.
- **Deny-by-default:** an unauthenticated / empty `CallerId` yields `ClientCompanyId == ""`, which
  matches nothing (column is NOT NULL). A wiring bug fails closed (empty result), never open.
- **No migration:** query filters are model metadata, not schema. Zero DB change.

### System-context bypass (explicit, auditable)

The 5 tenant-less lookups run under a pre-tenant / SNS-authenticated caller (no `ClientCompanyId`
header — `CallerId` empty at that point), so the filter above would wrongly return nothing. Each
must call `.IgnoreQueryFilters()` at the query, making the bypass structurally visible and grep-able:

- `TransactionRepository.GetByProviderReferenceIdAsync`
- `TransferRepository.FindByCircleTransferIdAsync`
- `RecipientRepository.FindByCircleRecipientIdAsync`
- `RedeemRequestRepository.FindByCircleRedeemIdAsync`
- `LinkedBankAccountRepository.FindByCircleBankAccountIdAsync`

These stay tenant-less by design (they DISCOVER the tenant); after discovery the webhook path
`Set`s the caller, and all subsequent queries flow through the filter for that tenant.

## Decisions to confirm before coding

1. **Filter source = `ICallerContext` (admin-bypass), not a new per-request tenant ambient.**
   Rationale: identity==tenant for regular callers makes the filter need nothing request-derived;
   admin's request-derived scope stays where it already lives (resolver + explicit predicate).
   Alternative rejected: a settable `TenantFilterContext { TenantId; Bypass }` ambient set in
   middleware — more moving parts, and admin's target tenant isn't known at middleware time.
2. **Keep the existing explicit `.Where(x.ClientCompanyId == ...)` predicates.** They are now
   redundant for regular tenants but carry admin single-tenant scoping the filter deliberately
   does not, and are harmless (double predicate). Not deleting in this slice.
3. **DbContext depends on `ICallerContext`** (Infrastructure → Application, allowed). DbContext
   and ICallerContext are both scoped — lifetimes match.

## Sections (review each before coding the next)

### S1 — DbContext filter wiring — REVIEW FIRST
- `DbContext` ctor takes `ICallerContext callerContext`; store in a field.
- In `OnModelCreating`, add `HasQueryFilter(e => callerContext.IsAdmin || e.ClientCompanyId ==
  callerContext.CallerId)` to each of the 11 entity configs.
- Verify EF re-evaluates the instance-member accessors per query (documented multitenant pattern).

### S2 — System-context bypass
- Add `.IgnoreQueryFilters()` to the 5 tenant-less lookup queries above. No signature change.

### S3 — Tests (Testcontainers / test-full — Api tier)
- Tenant A cannot read Tenant B's rows via a normally-scoped query (filter enforced).
- A system lookup (`GetByProviderReferenceIdAsync`) finds a row regardless of ambient caller.
- Admin read spans tenants (bypass path).
- Unit/Application tier cannot exercise the EF filter — verification is test-full, slower.

### S4 — Docs
- Note the mechanism in the DbContext filter region + update audit F7 to RESOLVED.
- INV7 gloss in CLAUDE.md already matches ("structurally impossible") — confirm, likely no edit.

## Not in scope
Removing explicit repo predicates (decision 2). A settable tenant ambient. F6 follow-ups.
