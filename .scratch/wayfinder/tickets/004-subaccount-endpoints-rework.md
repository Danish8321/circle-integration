---
title: Tenant scope resolution + sub-account endpoints rework (get/list/disable/enable/resubmit, Admin-only create)
label: done
status: closed
assignee: danish
blocked_by: []
parent: map.md
---

# Spec: Tenant scope resolution + sub-account endpoints rework

Folds Phase 1 Task 2 (tenant scope resolution + RFC 7807 exception taxonomy — never built) and Task 4 (sub-account endpoints rework) into one shippable slice, per decision 2026-07-17. Source: `docs/features/01-tenancy-and-authorization.md`,
`docs/features/07-sub-account-and-entity-registration.md` (old source
`docs/Phase_1_Feature_Slices.md` Tasks 2 & 4, deleted 2026-07-17 — superseded by the per-feature
doc restructure).

## Problem Statement

An operator (Admin) can create a sub-account today, but nobody can see one afterwards. There is no way to fetch a sub-account's lifecycle state or its latest entity-registration outcome (including a rejection reason), no way to list sub-accounts, no way to disable or re-enable one, and no way to resubmit a rejected entity registration. A client company whose registration was rejected is stuck permanently with no visibility and no recovery path.

Additionally, "which tenant does this request operate on" is decided ad hoc: the create endpoint takes `ClientCompanyId` from the request body with only an inline `IsAdmin` check in the controller. There is no single resolution rule, so every new endpoint risks inventing its own tenant-scoping logic — the exact class of bug PRD §2.4 forbids.

## Solution

One canonical tenant scope resolution rule, used by every endpoint that operates on a tenant:

- A **SubAccount** caller always resolves to its own `ClientCompanyId`; requesting any other tenant is forbidden (403).
- An **Admin** caller must name the target tenant explicitly (it has no tenant of its own); Admin list access resolves to "all tenants" and is itself audited.

On top of that rule, the sub-accounts API becomes a full lifecycle surface: create (Admin-only, explicit target), get one (with latest registration status and rejection reason), list (Admin-only, filterable by lifecycle state), disable/enable (Admin-only), and resubmit a rejected registration. All failures surface as RFC 7807 `ProblemDetails` produced centrally from a small domain-exception taxonomy — no controller catches anything.

## User Stories

1. As an Admin operator, I want sub-account creation to target an explicitly named `ClientCompanyId`, so that my Admin credential (which owns no tenant) can never be mistaken for the tenant being created.
2. As an Admin operator, I want a non-Admin attempt to create a sub-account rejected with 403, so that tenants cannot self-provision.
3. As a SubAccount caller, I want to fetch my own sub-account, so that I can see its lifecycle state and whether it is disabled.
4. As a SubAccount caller, I want the get response to include the latest entity-registration status and rejection reason, so that I know why my registration was rejected and what to fix.
5. As a SubAccount caller, I want any attempt to read another tenant's sub-account to fail with 403, so that cross-tenant access is structurally impossible.
6. As an Admin operator, I want to fetch any sub-account by explicitly naming its `ClientCompanyId`, so that I can support any tenant without impersonating it.
7. As an Admin operator, I want to list all sub-accounts, so that I can see the whole book of tenants at once.
8. As an Admin operator, I want to filter the list by lifecycle state (e.g. only Rejected), so that I can find tenants needing intervention.
9. As a SubAccount caller, I want list access denied with 403, so that no tenant can enumerate other tenants.
10. As an Admin operator, I want to disable a sub-account, so that a compromised or delinquent tenant is immediately cut off from operations.
11. As an Admin operator, I want to re-enable a previously disabled sub-account, so that a resolved tenant can resume operations.
12. As a SubAccount caller, I want disable/enable denied with 403, so that a tenant cannot toggle its own (or anyone's) operational status.
13. As a SubAccount caller with a Rejected registration, I want to resubmit corrected business details, so that I can recover from a rejection without operator involvement.
14. As a SubAccount caller, I want resubmission rejected with 409 when my sub-account is not in the Rejected state, so that an in-flight or approved registration cannot be clobbered.
15. As a SubAccount caller, I want to supply an idempotency key on resubmit, so that a retried request cannot create a duplicate registration at the provider.
16. As any caller, I want a request for a nonexistent sub-account to return 404 with an RFC 7807 body, so that "not found" is distinguishable from "forbidden".
17. As any caller, I want every error (403/404/409/422/5xx) to be an RFC 7807 `ProblemDetails` with a stable shape, so that my client error handling never parses ad-hoc payloads.
18. As an auditor, I want every Admin cross-tenant read and every mutating operation recorded in the audit log with the acting caller and target tenant, so that all-tenant access is itself traceable.
19. As a developer of any future endpoint, I want a single resolver for "which tenant does this request operate on", so that no handler can invent its own tenant-scoping rule.
20. As an operations engineer, I want provider failures during resubmit surfaced as distinct provider-rejected vs provider-unavailable errors, so that retryable and terminal failures are distinguishable.

## Implementation Decisions

- **Tenant scope resolver** (Application, Shared): one static resolution function taking the caller context and an optional requested `ClientCompanyId`, returning a `TenantScope`. Semantics (from the Phase 1 plan's prototype tests): SubAccount + no request = own id; SubAccount + own id = own id; SubAccount + other id = `TenantForbiddenException`; Admin + explicit id = that id; Admin + no id = all-tenants (valid only where the endpoint explicitly supports all-tenant scope, i.e. list). Every tenant-operating handler/endpoint goes through this — never reads a route or body tenant value directly.
- **`TenantScope` value type** (Application, Shared; codebase-design decision 2026-07-17, supersedes the plan doc's `string?`-with-null-means-all-tenants return): closed hierarchy — `Single(string ClientCompanyId)` | `AllTenants`. Single-tenant handlers accept a plain `ClientCompanyId` string extracted from `Single` at the endpoint, so they structurally cannot receive all-tenant scope; only the list handler matches on `TenantScope`. Prototype-derived shape:

  ```csharp
  public abstract record TenantScope
  {
      public sealed record Single(string ClientCompanyId) : TenantScope;
      public sealed record AllTenants : TenantScope; // Admin list only
  }
  ```
- **Exception taxonomy** (Application): sealed `TenantForbiddenException`, `NotFoundException`, `ConflictException`, `ProviderRejectedException`, `ProviderUnavailableException`, all deriving from one abstract domain-exception base. Mapped centrally to `ProblemDetails` (403/404/409/422/503 respectively) by a global exception handler in the Api tier. Controllers drop all inline `Problem(...)` responses and try/catch.
- **Module placement**: all new use cases live under the Compliance module per the B0.5 module-boundaries decision (the Phase 1 plan's older `SubAccounts/` namespace references are superseded).
- **Create rework**: command gains an explicit target `ClientCompanyId` distinct from the caller identity (kept for audit). Admin-only enforcement moves out of inline controller logic into the resolver/exception path.
- **Get one**: query by resolved `ClientCompanyId`; result carries sub-account id, tenant id, lifecycle state, disabled flag, provider wallet id, latest registration status, and rejection reason (joined from the latest entity registration). 404 when no sub-account exists for the scope.
- **List**: Admin-only (all-tenant scope), optional lifecycle-state filter, returns the same result shape as get-one per element.
- **Disable/enable**: one command with a boolean target state (idempotent set, not toggle), Admin-only, audited with correlation id.
- **Resubmit**: allowed only when the sub-account's lifecycle state is Rejected — otherwise `ConflictException`. Follows invariant 11: reserve → gateway call → complete with two `SaveChangesAsync`, consumer idempotency key required and forwarded to the provider. Creates a new entity registration attempt; sub-account transitions per the §3.2 state machine.
- **Routes**: resource-style under the sub-accounts root — get one and mutations address the tenant by explicit path segment (Admin) with resolver enforcement; disabled state set via an idempotent PUT sub-resource; resubmit as a POST action sub-resource. Versioned route prefix per the Phase 1 plan.
- **Repository surface**: sub-account repository gains a list operation (optionally state-filtered); entity-registration repository's latest-for-sub-account lookup is reused. No generic repository abstraction (invariant 1).
- **OpenAPI contract**: `contract.sh` re-emitted in the same commit as the endpoint changes; the emitted document is the contract of record for the new endpoints.

## Testing Decisions

- Good tests here assert **external behavior at the seam**: status codes, `ProblemDetails` shape, response body fields, and DB state after mutation — never handler internals, never mock-call counts as the primary assertion.
- **Primary seam — HTTP integration** (existing `WebApplicationFactory` + Testcontainers SQL Server factory; prior art: existing sub-accounts controller integration tests): full matrix of caller role × endpoint — SubAccount reads own (200), SubAccount reads other (403 ProblemDetails), Admin explicit target (200), Admin list + filter, non-Admin create/list/disable (403), missing sub-account (404), resubmit on non-Rejected (409), disable→get round trip shows disabled flag, resubmit happy path persists a new registration row. In-memory EF provider forbidden — Testcontainers only.
- **Secondary seam — handler/resolver unit tests** (prior art: existing create-handler unit tests, mocked ports): resolver truth table (five cases above), get-one mapping of latest registration status + rejection reason, resubmit conflict branch, resubmit reserve/complete idempotency branching. Only branches awkward to force over HTTP.
- Architecture tests (existing project) continue to enforce tier rules; no new seam.

## Out of Scope

- Webhook pipeline (Task 5) — registration status changes arriving asynchronously from Circle are not consumed here; resubmit only initiates.
- Mock provider gateway rework / simulated webhooks (Task 6) — existing fake gateway stands.
- Deposit addresses, ledger, recipients, transfers, redemption (Tasks 7–11).
- Admin master-account summary views (Task 12) — list here is the raw sub-account list only.
- Deleting sub-accounts — no such operation exists in the PRD; disable is the only off-switch.
- Any client project or generated client — repo is API-only; `contract.sh` client step stays a stub.

## Further Notes

- The Phase 1 plan file paths for Task 2/4 predate the B0.5 module-boundaries decision and the code as committed (e.g. `Controllers/RedeemController.cs` doesn't exist); treat its interfaces and step semantics as authoritative, not its paths.
- Invariant 12 (no Travel Rule fields on transfers) is untouched by this slice but the resubmit gateway request must stay within the already-verified Circle entity-registration field set — re-verify against live Circle docs before adding any request field the current gateway doesn't send.
- The inline `IsAdmin` check currently in the create endpoint is deleted, not kept alongside the resolver — one enforcement path only.
