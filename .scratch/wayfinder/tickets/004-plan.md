---
title: Implementation plan — ticket 004 (tenant scope resolution + sub-account endpoints rework)
label: wayfinder:plan
status: closed
parent: 004-subaccount-endpoints-rework.md
---

# Plan: ticket 004 — small TDD tasks

Method: full TDD per task — write the failing test first, watch it fail, implement, watch it pass.
Each task is one commit. Verification script named per task; no "done" claim without it.

Executor: `task-executor` subagent per task, two-stage review in main session
(spec compliance, then Invariants + `check.sh`).

## Task 1 — `TenantScope` + resolver (unit-first)

- **Test first**: `tests/TreasuryServiceOrchestrator.UnitTests/Shared/TenantScopeResolverTests.cs`
  — truth table: SubAccount+null=own; SubAccount+own=own; SubAccount+other=`TenantForbiddenException`;
  Admin+explicit=that id; Admin+null=`AllTenants`.
- **Then**: `src/.../Application/Shared/TenantScope.cs` (abstract record, `Single(string ClientCompanyId)` |
  `AllTenants`, per ticket prototype), `src/.../Application/Shared/TenantScopeResolver.cs`
  (static `Resolve(ICallerContext caller, string? requestedClientCompanyId)`),
  `src/.../Application/Exceptions/TenantForbiddenException.cs` (temporary base `Exception`; re-based in Task 2).
- **Verify**: `test-fast.sh` — resolver tests red then green.

## Task 2 — Exception taxonomy + central RFC 7807 mapping

- **Test first**: extend `tests/.../UnitTests/` with `Api/DomainExceptionHandlerTests.cs`
  (direct `TryHandleAsync` unit tests): TenantForbidden→403, NotFound→404, Conflict→409,
  ProviderRejected→422, ProviderUnavailable→503, each a `ProblemDetails` with Title/Status/Detail.
- **Then**: `src/.../Application/Exceptions/DomainException.cs` (abstract base) +
  sealed `NotFoundException`, `ConflictException`, `ProviderRejectedException`,
  `ProviderUnavailableException`; re-base `TenantForbiddenException` and
  `SubAccountAlreadyExistsException` (derives `ConflictException` semantics — keep type, map 409).
  Update `src/.../Api/Middleware/DomainExceptionHandler.cs` to map the base taxonomy centrally.
- **Verify**: `test-fast.sh`.

## Task 3 — Domain lifecycle transitions

- **Test first**: extend `tests/.../UnitTests/Domain/SubAccountTests.cs` and `EntityRegistrationTests.cs`:
  `MarkRejected` (PendingCompliance→Rejected only), `SetDisabled(bool)` (idempotent, any state),
  `ResubmitCompliance` (Rejected→PendingCompliance only, invalid from others);
  `EntityRegistration.Reject(reason, nowUtc)` sets Status+RejectionReason+UpdatedAtUtc.
- **Then**: `src/.../Domain/SubAccount.cs`, `src/.../Domain/EntityRegistration.cs`.
- **Verify**: `test-fast.sh`.

## Task 4 — Repository surface widening

- **Change**: `src/.../Application/Compliance/Ports/ISubAccountRepository.cs` gains
  `ListAsync(SubAccountLifecycleState? filter, ct)`;
  `IEntityRegistrationRepository.cs` gains `GetLatestForSubAccountAsync(Guid subAccountId, ct)`.
  Implement in `src/.../Infrastructure/Persistence/SubAccountRepository.cs`,
  `EntityRegistrationRepository.cs` (latest = max `CreatedAtUtc`). No generic repository (invariant 1).
- **Test**: exercised by Tasks 6–9 integration tests (Testcontainers); this task's gate is compile-only.
- **Verify**: `check.sh` on both projects.

## Task 5 — Create rework: resolver in, inline `IsAdmin` out

- **Test first**: update `tests/.../IntegrationTests/Compliance/SubAccountsControllerTests.cs`:
  non-Admin create → 403 `ProblemDetails` (via taxonomy, not inline `Problem`);
  Admin create with explicit target still 201. Update
  `tests/.../UnitTests/Compliance/CreateSubAccountHandlerTests.cs` for new command shape.
- **Then**: `CreateSubAccountCommand` gains explicit `TargetClientCompanyId` (caller id kept for audit);
  `src/.../Api/Compliance/SubAccountsController.cs` — delete inline `IsAdmin` block (one enforcement
  path only), resolve scope via `TenantScopeResolver`, route gets versioned prefix `v1/sub-accounts`.
- **Verify**: `test-fast.sh` + `test-full.sh` (integration).

## Task 6 — Get one (vertical slice)

- **Test first**: integration — SubAccount reads own → 200 with lifecycle state, disabled flag,
  wallet id, latest registration status + rejection reason; SubAccount reads other → 403;
  Admin explicit target → 200; missing → 404 `ProblemDetails`. Unit — handler maps latest
  registration fields; no registration yet → nulls.
- **Then**: `src/.../Application/Compliance/GetSubAccount/{GetSubAccountQuery,GetSubAccountHandler,SubAccountDetailsResult}.cs`;
  controller `GET v1/sub-accounts/{clientCompanyId}`; response DTO in Api tier maps result (invariant 5).
- **Verify**: `test-fast.sh` + `test-full.sh`.

## Task 7 — List (Admin-only, state filter)

- **Test first**: integration — Admin list all → 200 array of same element shape as get-one;
  `?state=Rejected` filters; SubAccount caller → 403.
- **Then**: `src/.../Application/Compliance/ListSubAccounts/{ListSubAccountsQuery,ListSubAccountsHandler}.cs`
  (matches on `TenantScope`, only handler allowed `AllTenants`); controller `GET v1/sub-accounts`;
  Admin all-tenant read audited (invariant 8).
- **Verify**: `test-fast.sh` + `test-full.sh`.

## Task 8 — Disable/enable (idempotent PUT)

- **Test first**: integration — Admin `PUT v1/sub-accounts/{id}/disabled` `{"disabled":true}` → 200;
  get shows disabled flag; repeat PUT idempotent; re-enable round trip; SubAccount caller → 403;
  missing → 404. Audit row asserted with acting caller + target tenant.
- **Then**: `src/.../Application/Compliance/SetSubAccountDisabled/{SetSubAccountDisabledCommand,Handler}.cs`;
  controller PUT sub-resource.
- **Verify**: `test-fast.sh` + `test-full.sh`.

## Task 9 — Resubmit rejected registration

- **Test first**: unit — conflict branch (non-Rejected → `ConflictException`),
  reserve→gateway→complete ordering with two `SaveChangesAsync`, idempotency-key branching
  (mocked ports, prior art `CreateSubAccountHandlerTests`). Integration — seed Rejected sub-account
  (Task 3 transitions), `POST v1/sub-accounts/{id}/registrations` with `Idempotency-Key` → 201,
  new registration row persisted, state PendingCompliance; non-Rejected → 409; provider-unavailable → 503.
- **Then**: `src/.../Application/Compliance/ResubmitEntityRegistration/{Command,Handler,Result,Validator}.cs`;
  gateway request stays within verified Circle field set (ticket note — no new request fields);
  controller POST action sub-resource.
- **Verify**: `test-fast.sh` + `test-full.sh`.

## Task 10 — Contract re-emit + full gate

- **Change**: run `contract.sh` — emitted `docs/openapi/openapi.json` is contract of record,
  committed with endpoint changes (same-slice rule). Full sweep: `check.sh`, `test-fast.sh`,
  `test-full.sh` (arch tests included).
- **Verify**: `contract.sh` diff clean + `test-full.sh` green.

## Status

- [x] Task 1 — red: 10 compile errors; green: 20/20 `test-fast.sh`, `check.sh` clean. NOTE: `TenantScope.Single` shipped as `SingleTenant` (CA1720 vs warnings-as-errors); Tasks 5–7 must reference `SingleTenant`.
- [x] Task 2 — red: 4 CS0246; green: 27/27 `test-fast.sh`, 6/6 `test-full.sh`, `check.sh` clean. Commit d44b2e3. Side fix 2306aae: pre-existing non-admin-403 integration test missing Idempotency-Key (latent since d8856a0). Env fix: broken user-scope DOCKER_HOST removed (this session's shells still inherit it — prefix `env -u DOCKER_HOST`).
- [x] Task 3 — red: 17 CS1061; green: 40/40 `test-fast.sh`, `check.sh` clean. Commit 40c3a95.
- [x] Task 4 — inline (mechanical). `check.sh` Infrastructure clean, 40/40 fast, 6/6 full. Commit f8732ab.
- [x] Task 5 — red: 5 route 404s + CS1729; green: 41/41 fast, 7/7 full, `check.sh` clean. Commit 0873bdb. Admin-only enforced in handler (injected ICallerContext); command kept `ClientCompanyId` name + doc comment; DomainExceptionHandler emits application/problem+json.
- [x] Task 6 — red: CS0246 unit + 4×404 integration; green: 44/44 fast, 11/11 full, `check.sh` App+Api clean. Commit b50d14d.
- [x] Task 7 — red: missing types + 5×405; green: 49/49 fast, 16/16 full, `check.sh` clean. Commit 7847ee2. Query gained CorrelationId (audit signature). N+1 latest-registration lookup in list — fine at admin scale, candidate for later join.
- [x] Task 8 — red: CS0246 both seams; green: 54/54 fast, 21/21 full, `check.sh` clean. Commit 8be908f. JsonRequired on Disabled prevents under-posting.
- [x] Task 9 — red: CS0246; green: 5/5 handler unit (59 fast total), 25/25 full, `check.sh` clean. Commit 0f1448b. First executor died at session limit before writing anything; redispatched clean.
- [x] Task 10 — full gate green: 4 projects `check.sh` clean, 59 fast, 25 full. Contract emitted (4 paths, WeatherForecast dropped), commit 46c7b0b. FOLLOW-UP (commits cd5fe3b + build-time-emit): `contract.sh` no longer runs the API as a server + curls loopback (that hung in this sandbox). It now emits the doc at build time in-process via `Microsoft.Extensions.ApiDescription.Server` (GetDocument.Insider) — no port, no loopback, works in CI and sandbox alike. `contract.sh` proven green (exit 0, no diff, run twice). Regenerated `openapi.json` dropped the WebApplicationFactory-only `servers:[http://localhost/]` block. `.gitattributes` pins `docs/openapi/*.json` to LF for a deterministic diff gate under `core.autocrlf=true`.
