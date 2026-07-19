# TreasuryServiceOrchestrator — Architecture & Fintech Review
Reviewer: Technical Architect (fintech) · Date: 2026-07-19

## Executive Summary

Codebase is well past greenfield despite `.claude/CLAUDE.md` still saying "no code yet" — 404 source files across four Clean Architecture tiers, 125 test files, 10 EF migrations. Overall health: **solid, disciplined, production-adjacent but not production-ready.** Architecture invariants are enforced not just by convention but by `NetArchTest`-based fitness tests (`DependencyRuleTests.cs`), which is unusually rigorous. Money handling, idempotency, tenant isolation, and audit immutability are all implemented correctly and match the documented invariants.

The one severe gap: **webhook signature verification is a stub that always returns `true`, wired unconditionally in all environments including Production.** Circle webhooks (deposits, transfers, payouts, compliance decisions) drive money-moving state transitions — this is a spoofable trust boundary today. Everything else is either done well or explicitly and honestly deferred (ADR-tracked).

**Recommendation: do not go to Production before closing the SNS signature-verification gap.** Everything else can ship incrementally.

## Architecture Assessment

- Layering is exactly as documented: `Domain` (flat, zero framework refs) ← `Application` (ports/handlers/dtos by kind) ← `Infrastructure` (EF, Circle gateways) ← `Api` (thin controllers, DI wiring). Verified by reading, not just docs — `SubAccountsController` dispatches to handlers and does nothing else; `CreateTransferCommandHandler` orchestrates but has zero HTTP/EF awareness.
- The four `DependencyRuleTests` fitness tests actively fail the build if Domain references EF/ASP.NET, if Application references Infrastructure/Api, or if the retired module-namespace axis (`Compliance`/`Ledger`/`Webhooks`/`Admin`) creeps back. This is real enforcement, not aspirational documentation — rare to see in a repo this size.
- Gateway/port split (`ISubAccountGateway` vs `IStablecoinGateway`) matches ADR 0006 and is honored in DI (`CircleIntegrationServiceCollectionExtensions.cs`), including the subtle correctness rule that both Circle gateways must be faked together in Development to avoid a fake-vs-live cross-wiring hazard (comment at line 34-35 shows real awareness of a specific past incident/risk).
- Circuit breaker scope is deliberately per-typed-client (documented trade-off in `CircleResiliencePipelineFactory.cs`), not provider-wide — a reasonable, explicitly-reasoned choice.

## Invariant-by-Invariant Compliance

| # | Invariant | Status | Evidence |
|---|---|---|---|
| 1 | No generic `IRepository<T>` | ✅ Pass | Each repo (`SubAccountRepository.cs`, `TransferRepository.cs`, …) is use-case-shaped, implements its own port interface. |
| 2 | `TimeProvider`, never `DateTime.Now/UtcNow` | ✅ Pass | Grep across `src/` found zero real usages — only doc-comment mentions. Handlers inject `TimeProvider` (e.g. `CreateTransferCommandHandler.cs:25`, used at `:81`). |
| 3 | `IHttpClientFactory`, never `new HttpClient()` | ✅ Pass | `HttpNotificationSender.cs` and Circle gateways registered via `AddHttpClient<...>` (`CircleIntegrationServiceCollectionExtensions.cs:41-44`). No `new HttpClient()` instantiations found in `src/`. |
| 4 | `CancellationToken ct = default` on every handler | ✅ Pass | All 35 files under `Application/Handlers/` have the pattern; verified by grep count (35/35). |
| 5 | No Domain entity leaks past Application into API | ✅ Pass | Controllers return `*Response` records (`SubAccountResponse`, `TransferResult`-mapped DTOs); no controller returns `ActionResult<SubAccount>` etc. — confirmed absent. |
| 6 | Validation filter on every endpoint, RFC 7807 only | ✅ Pass | `ValidationActionFilter.cs` runs `IValidator<T>` globally per action arg; `DomainExceptionHandler.cs` centrally maps the full exception taxonomy to `ProblemDetails`. No controller has an inline try/catch of a domain exception (spot-checked `SubAccountsController.cs`). |
| 7 | Tenant id always from `ICallerContext`, never route/body | ✅ Pass, strongly | `TenantScopeResolver.Resolve` (`Application/Services/TenantScopeResolver.cs`) is the single choke point; every controller action calls it before building a command. **Additionally enforced at the data layer** — every tenant-owned entity has `HasQueryFilter(x => callerContext.IsAdmin || x.ClientCompanyId == callerContext.CallerId)` in `TreasuryServiceOrchestratorDbContext.cs` (12 occurrences). This is defense-in-depth beyond what the invariant literally requires — a forgotten `.Where()` cannot leak cross-tenant data. |
| 8 | Admin never impersonates, always audited | ✅ Pass (structurally) | `TenantScopeResolver` gives Admin `AllTenants` or an explicit `SingleTenant(requestedId)` — never silently assumes an identity. Audit trail exists (`AuditRecord`) though I did not verify every admin-scoped handler writes one — spot-check only. |
| 9 | Mock mode structurally blocked in Production | ✅ Pass | `MockModeGuard.Validate` throws `InvalidOperationException` if `mockModeEnabled && environment == Production` (`MockModeGuard.cs:11-18`), called unconditionally at startup in `CircleIntegrationServiceCollectionExtensions.cs:26` — not gated behind config alone. |
| 10 | `Money(decimal, string)` only monetary type | ✅ Pass | Domain `Money.cs` is the sole record; EF maps it via `ComplexProperty` with `HasPrecision(28, 8)` everywhere (`TreasuryServiceOrchestratorDbContext.cs`) — no `double`/`float` money found. |
| 11 | reserve → gateway → complete, two `SaveChangesAsync`, idempotency key | ✅ Pass | `IdempotencyExecutor.ExecuteAsync` (`Application/Services/IdempotencyExecutor.cs`) is the single implementation of this shape, used by every mutating handler I checked (`CreateTransferCommandHandler`, `CreateRedemptionCommandHandler`). SaveChanges #1 persists the reservation before the gateway call; SaveChanges #2 commits completion + staged work atomically. |
| 12 | No Travel Rule originator fields on transfer | ✅ Pass | `CreateTransferCommand.cs` and `CreateTransferRequest.cs` carry only `RecipientId`/`Amount`/keys — doc comment explicitly calls out the omission is intentional. |

**12/12 documented invariants hold**, several (7, 9) with defense-in-depth beyond the letter of the rule.

## Fintech / Business-Critical Findings (ranked by severity)

### 🔴 Critical — Webhook signature verification is a permanent stub
`ISnsSignatureVerifier` has exactly one implementation in the whole codebase: `MockSnsSignatureVerifier.VerifyAsync` returns `Task.FromResult(true)` unconditionally (`Infrastructure/Webhooks/MockSnsSignatureVerifier.cs`). It is registered in `CircleIntegrationServiceCollectionExtensions.cs:11` **outside** the mock/dev/production branching — every environment, including a hypothetical Production deploy today, accepts any POST to `/v1/webhooks/circle` claiming to be a valid SNS message and drives real state transitions (deposit confirmations, transfer/payout status, compliance decisions) off it. The code is honest about this (comment: "Phase 1 stand-in... Phase 3 scope," backed by ADR 0009 committing to a real AWS SNS verifier), but there is currently no code-level gate analogous to `MockModeGuard` preventing this stub from reaching Production. **This is the one finding that should block a Production cutover.**
- Fix: implement the real cert-domain + SHA1/SHA256 SNS verification (already scoped by ADR 0009/ticket 21), and add an equivalent hard environment guard (`SnsVerifierGuard`, mirroring `MockModeGuard`) so a missing real implementation fails startup in Production rather than degrading silently.

### 🟡 Medium — Secrets management not yet implemented
ADR 0009 commits to AWS Secrets Manager for Circle API credentials in Production, but no `AWSSDK.SecretsManager`/`Amazon.Extensions.Configuration.SecretsManager` reference exists yet (`appsettings.json` `ApiKey` is empty, which is correct for a repo, but there's no wiring for where the real value comes from in Production). Not a defect — explicitly Phase 3 scope — but worth tracking so it doesn't slip the same way signature verification risks slipping.

### 🟢 Low — RedeemRequest.Fees/NetAmount zero-value JSON quirk
`TreasuryServiceOrchestratorDbContext.cs:253-262` documents a real EF Core limitation (optional complex property + zero-value sentinel ambiguity) and works around it correctly by mapping to a JSON column. Good catch, well-documented, no action needed — flagging only because it's exactly the kind of subtle money-precision bug that matters in this domain and the team clearly already tested for it empirically.

### 🟢 Low — Circuit breaker isolation trade-off
Per-typed-client breakers (not provider-wide) mean a full Circle outage trips two breakers independently rather than one. Documented and accepted trade-off (`CircleResiliencePipelineFactory.cs`); revisit only if provider-wide outages become the dominant failure mode, as the comment already says.

### Money safety / idempotency / audit — no issues found
- Idempotency: single well-tested implementation (`IdempotencyExecutor`), unique index on `(TenantId, IdempotencyKey)` (`TreasuryServiceOrchestratorDbContext.cs:84`), correct replay-vs-in-flight-retry branching.
- Audit immutability: real SQL Server `INSTEAD OF UPDATE, DELETE` trigger (`AddAuditRecordImmutabilityTrigger` migration) — not just an app-level convention. Verified by reading the migration SQL directly.
- Precision: `HasPrecision(28, 8)` consistently applied to every `Money` mapping — sufficient for USDC's 6-decimal precision with headroom.

## Coding Standards & Quality

- Domain entities are properly encapsulated: private setters, static `Create` factories, guarded state transitions that throw `InvalidOperationException` on illegal transitions (`SubAccount.cs`, `RedeemRequest.cs`). No anemic-domain-model smell.
- Naming is consistent and unabbreviated project-wide (matches the repo's own no-invented-abbreviations convention).
- XML doc comments on handlers/services are unusually good — they explain *why*, not *what* (e.g. `CreateRedemptionCommandHandler.cs`'s comment on why `SourceWalletId` must always be explicit, tied to a named invariant and hazard family). This is genuinely above-average documentation discipline for a codebase this size.
- No dead code, no commented-out blocks, no TODO-without-ticket found in the files reviewed.
- One nit: `SubAccount.SetDisabled(bool disabled)` is a plain setter-with-a-verb rather than two named transitions (`Enable()`/`Disable()`) — inconsistent with the otherwise-strict guarded-transition style elsewhere in the same file. Minor; not a correctness issue.

## Test Coverage Assessment

- Tiering matches the documented strategy exactly: `UnitTests` (xUnit v3 + Moq, mocked ports) for Domain/Application, `IntegrationTests` (WebApplicationFactory + real Testcontainers SQL Server, confirmed in `TreasuryServiceOrchestratorApiFactory.cs` — genuinely spins up `MsSqlContainer`, no in-memory EF fake), `ArchitectureTests` (NetArchTest fitness functions) as a distinct project — this is a mature test pyramid.
- Every handler I sampled (`CreateTransferCommandHandlerTests.cs`, `CreateRedemptionCommandHandlerTests.cs`, etc.) has a corresponding unit test file — coverage looks systematic rather than spotty (32 handler files, ~32 matching test files by name).
- Integration coverage includes tenant isolation specifically (`TenantIsolationQueryFilterTests.cs`) and webhook dedup (`WebhookDedupTests.cs`) and outbox atomicity (`NotificationOutboxAtomicityTests.cs`) — the tests target exactly the business-critical seams a fintech reviewer would ask about.
- Gap: no test file targets `MockSnsSignatureVerifier` bypassing signature checks as a security scenario, unsurprising since it's a known stub — but once the real verifier lands, a test asserting invalid-signature → `403 Forbidden` should be a release gate.

## Recommendations (prioritized)

1. **Before Production:** implement real AWS SNS signature verification and add a `MockModeGuard`-style hard environment check so the stub cannot reach Production even by DI misconfiguration.
2. Wire AWS Secrets Manager per ADR 0009 before the first real Circle production credential is issued.
3. Add an integration test asserting webhook `Receive` returns 403 on an invalid/unsigned envelope once the real verifier exists — turns this review's one finding into a permanent regression gate.
4. Minor: align `SubAccount.SetDisabled` with the codebase's own guarded-transition convention (`Disable()`/`Enable()`) for consistency — cosmetic, no rush.
5. Update `.claude/CLAUDE.md`'s stale "Greenfield: no code yet" line — it currently misleads anyone (including an AI assistant) reading it fresh, as this review's own false start demonstrated.

---
*Scope note: reviewed all Domain entities/value objects, all Application handlers/services/ports (spot-checked in depth, full file listing enumerated), Infrastructure persistence/DI/resilience/webhook/mock layers, Api middleware/controllers/DI, and a representative sample of tests. Did not execute `check.sh`/`test-fast.sh`/`test-full.sh` — findings are from static reading, not verified build/test runs.*
