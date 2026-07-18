# TreasuryServiceOrchestrator — Documentation Index

| | |
|---|---|
| **Status** | Restructured 2026-07-17 from the original monolithic `PRD.md` / `Phase_1_Feature_Slices.md` / `Phase_3_Circle_Integration_Plan.md` / `DepositReconciliationPLan.md` into one file per feature, to stop the same fact (e.g. "which gateway owns `RegisterRecipientAsync`") drifting across 4+ places. Those four files are superseded and deleted — history is in git. |
| **Provider facts source** | Circle Mint developer documentation (Institutional API). Verified against the live site 2026-07-07 (full pass), re-verified 2026-07-17 (26-claim targeted pass, 0 discrepancies), and re-verified again per-feature during this restructure — each file below states its own live-verification results and citations. |

This is the entry point. Read it top to bottom once; after that, jump straight to the feature
file you need — each one is a complete, self-contained story (domain design through real Circle
HTTP integration) for its slice.

---

## 1. Vision & goals

TreasuryServiceOrchestrator is a **provider-agnostic treasury orchestration API**: internal
applications manage stablecoin treasury operations — minting, redemption, on-chain transfers,
fiat/stablecoin deposits — for institutional **sub-accounts**, through one unified REST interface.
Circle Mint's Institutional API (as a Circle Mint *Distributor*) is the first provider; the
product is defined in capability terms, not Circle endpoints, so a future provider (Fireblocks,
Bridge) can be added behind the same consumer-facing API without consumer rewrites.

**Non-goals (v1):** multi-provider routing, EURC/non-USDC assets, internal maker-checker
approval workflows (Circle's own Mint Console approval is the human gate), any end-customer-facing
surface, Express routes/local-currency swaps/Stablecoin Payins-Payouts/Credit API (Circle
documents these unsupported for External Entities).

## 2. Actors & roles

| Actor | Kind | Interaction |
|---|---|---|
| **APISO Portal Admin** | Internal user | Creates sub-accounts; operates on any sub-account; manages linked bank accounts; views everything. |
| **Legacy Applications** | Internal machine callers (ASP.NET 4.x) | Each maps to exactly one sub-account, operates only on it. |
| **Circle** | External provider | Executes treasury operations; delivers async events via webhooks. |
| **Operations / Finance** | Internal users | Consume audit records, reconciliation alerts, reports (via portal, not directly). |

Two roles, no finer-grained user-level RBAC — the calling application is the security principal:
**Admin** (all sub-accounts, sub-account creation, linked bank accounts, webhook replay) and
**SubAccount** (its own sub-account only, all reads + money-moving ops). Full design, the
`TenantScope` closed hierarchy, and caller-identity-vs-target-scope resolution rules:
**[`features/01-tenancy-and-authorization.md`](features/01-tenancy-and-authorization.md)**.

## 3. Module boundaries

Clean Architecture (Domain ← Application ← Infrastructure, Api wires all three) with Vertical
Slice as the work-breakdown lens. `Application`/`Domain` split into four named module
sub-namespaces plus one cross-cutting — decided in
**[`adr/0001-module-boundaries.md`](adr/0001-module-boundaries.md)**, not restructured here:

| Module | Owns | PRD-era sections | Feature files |
|---|---|---|---|
| `Compliance` | SubAccount, EntityRegistration lifecycle | §3–4 | 07 |
| `Ledger` | Wallet, FundAccount, DepositAddress, Transaction, BalanceSnapshot, LinkedBankAccount, Recipient, Transfer, RedeemRequest | §6–9 | 04, 08, 09, 10, 11 |
| `Webhooks` | Durable inbox, dedup, per-topic processors, notification outbox | §10, §10.1 | 03, 13 |
| `Admin` | Cross-tenant / master-account read views | §2.5 | 12 |
| `Shared` | Cross-cutting auth (`ICallerContext`, `TenantScopeResolver`), cross-module provider ports, shared config | n/a | 01, 02, 05, 06 |

New ports/handlers go under their module sub-namespace (`Application/<Module>/Ports`,
`Application/<Module>/<UseCase>`) — never a flat `Application/<UseCase>/` bag. This was a real,
repeatedly-drifted bug in the old docs; the feature files below use the corrected paths
throughout.

## 4. Build / read order

**Foundation files** — read first; every feature file depends on these and references them by
path rather than re-deriving their content:

| # | File | Covers |
|---|---|---|
| 1 | [`01-tenancy-and-authorization.md`](features/01-tenancy-and-authorization.md) | Caller registry, `ClientCompanyId`, Admin/SubAccount roles, request scoping, `TenantScope`. |
| 2 | [`02-mock-mode.md`](features/02-mock-mode.md) | Simulated-provider design, production guard. |
| 3 | [`03-webhook-processing.md`](features/03-webhook-processing.md) | Durable inbox, dedup, per-topic dispatch, SNS v1 transport + signature verification, subscription handshake. |
| 4 | [`04-ledger-and-balances.md`](features/04-ledger-and-balances.md) | `Transaction`/`BalanceSnapshot`/`FundAccount` domain, shared ledger-posting module, balance/history reads. |
| 5 | [`05-reliability-and-error-handling.md`](features/05-reliability-and-error-handling.md) | Idempotency, RFC 7807 taxonomy, `CircleErrorTranslator`, provider resilience (Polly), reconciliation job mechanics. |
| 6 | [`06-audit-and-compliance.md`](features/06-audit-and-compliance.md) | Audit records, correlation ids, Travel Rule posture, secrets/mTLS. |

**Feature capability files** — each a complete PRD-requirement → mock-impl → real-Circle-impl
story, Circle facts verified live during authoring:

| # | File | Covers |
|---|---|---|
| 7 | [`07-sub-account-and-entity-registration.md`](features/07-sub-account-and-entity-registration.md) | External Entities lifecycle: `Created → PendingCompliance → Active/Rejected`, resubmission, `Disabled` overlay. |
| 8 | [`08-banking-and-wire-instructions.md`](features/08-banking-and-wire-instructions.md) | `LinkedBankAccount`, Distributor + entity-scoped wire instructions, `wire` webhook topic. |
| 9 | [`09-deposits-and-funding.md`](features/09-deposits-and-funding.md) | Deposit address generation, fiat-wire (`deposits` topic) + on-chain (`transfers` topic) crediting. |
| 10 | [`10-outbound-transfers-and-recipients.md`](features/10-outbound-transfers-and-recipients.md) | Recipient registration/approval (`addressBookRecipients`), outbound transfers, Travel Rule posture. |
| 11 | [`11-redemption-and-payouts.md`](features/11-redemption-and-payouts.md) | Redemption/payout via `POST /v1/businessAccount/payouts`, gross/fees/net, `payouts` webhook topic. |
| 12 | [`12-admin-cross-tenant-views.md`](features/12-admin-cross-tenant-views.md) | All-sub-accounts view, drill-down, all-tenant transactions, Master Account summary. |
| 13 | [`13-internal-notifications-outbox.md`](features/13-internal-notifications-outbox.md) | Outbox pattern, background dispatcher, stub receiver — the five PRD-demo-required state-change notifications. |

**Not restructured, stay as-is:** [`../CONTEXT.md`](../CONTEXT.md) (glossary, single-context
domain reference — every feature file links into it rather than duplicating terms),
[`adr/`](adr/) (7 accepted decision records, already atomic), `circle-mint-docs/` (raw Circle
mirror, refreshed as needed, never final authority — see its own `README.md` for the
live-source priority order).

## 5. Delivery phasing

| Phase | Goal |
|---|---|
| **Phase 1** | Complete end-to-end flow on the mock provider (working demo, no Circle dependency). Demo script: create sub-account → screening `ACCEPTED` (+ one `REJECTED` + resubmit) → deposit address → simulated deposit credits ledger → recipient registration + approval → outbound transfer completes → redemption completes with gross/fees/net → tenant isolation holds → admin sees all + master summary → every step in transactions/balance history/audit records → each of the five state changes also arrives at the internal-notification stub receiver. |
| **Phase 2** | Hardening: reconciliation job live (before real money moves), webhook + notification dead-letter/replay, provider resilience (Polly), scheduled balance snapshots, observability completion, full list-endpoint pagination/filtering. |
| **Phase 3** | Real Circle integration: sandbox → production. Real HTTP gateway, SNS webhook signature verification, secrets in managed store, sandbox demo-script run with a real Mint Console approval, production cutover checklist. *(Previously a separate `Phase_3_Circle_Integration_Plan.md` — now inlined per feature: each feature file's own "real Circle HTTP integration" section carries this content.)* |
| **Deferred (roadmap)** | Internal maker-checker approvals, per-sub-account limits, additional notification channels (email/Teams), reporting/exports (CSV, CAMT.053), EURC + additional chains, additional providers, additional fiat rails (ACH/SEPA), user-level RBAC. |

## 6. Non-functional requirements

TLS 1.2+ in transit, encryption at rest, managed secret store with rotation, no secrets in
config/logs; structured logging with correlation ids on every request/event; stateless API tier;
single region; RPO 15 min / RTO 4 hr with tested backups; REST + JSON, URI versioning
(`/api/v1/...`), OpenAPI published; `Money` exact-decimal, explicit currency code, no
floating-point money anywhere; audit retention 7 years, immutable.

## 7. Known open items surfaced during this restructure

Each feature file logs its own open corrections/decisions; the ones with product-level (not just
doc-level) impact, not to be lost:

- **07**: ~~`ISubAccountGateway` in shipped code has no `GetExternalEntityAsync`~~ — **resolved**:
  implemented on `FakeSubAccountGateway`/`CircleSubAccountGateway`, plus
  `ISubAccountRepository.GetByCircleWalletIdAsync`, the `ProcessExternalEntityDecision` handler,
  and `SubAccount.MarkAccepted()`/`EntityRegistration.Accept()` domain transitions — the
  compliance-acceptance edge (`PendingCompliance → Active`) now works end to end, covered by
  unit tests. Migration `20260717153028_InitialCreate` generated (staged, not applied to a DB).
- **08**: ~~`GetWireInstructionsAsync` has no port method in shipped code yet~~ — **resolved**:
  implemented (`IStablecoinGateway.GetWireInstructionsAsync`, `CircleMintGateway`,
  `MockStablecoinGateway`, `GetWireInstructionsQueryHandler`) — this note was stale as of the
  2026-07-18 audit. Real Circle wire-creation body's region-dependent schema fidelity is
  Phase 3 ticket 21's sandbox-verification concern, not an unbuilt port.
- **03**: ~~unhandled webhook topics currently map to `Failed` → HTTP 500 → infinite SNS
  redelivery~~ — **resolved**: `WebhookProcessor` now has a distinct `WebhookProcessingStatus.
  Unhandled` value, mapped to HTTP 200 by `CircleWebhooksController` (no dead-letter for
  no-processor-registered topics). Durable inbox (`WebhookInboxEntry` + unique-index dedup via
  `WebhookInboxRepository`), `IWebhookTopicProcessor` dispatch seam, and the `externalEntities`
  processor are implemented and unit- and integration-tested (`test-full.sh` 29/29, including
  redelivery dedup and the tenant-scoping bypass); `ISnsSignatureVerifier` is a Phase-1 mock
  (always-valid) — real AWS SNS cert/signature verification remains Phase 3 scope, as designed.
  ~~no concrete dead-letter threshold/alerting number specified anywhere~~ — **resolved**:
  count-based, `Attempts >= 5` (`WebhookDeadLetterPolicy`), logged via a structured warning (no
  external alerting channel exists in this repo yet — the log is the current hook). **Decided
  and explicitly deferred** (not oversights): admin replay endpoint — no `Admin` module exists
  yet, replay is its natural home per B0.5, building a one-off elsewhere was rejected; SNS
  `SubscriptionConfirmation` handshake automation — untestable against Phase 1 mock mode until a
  real SNS subscription exists in Phase 3. `429` in the Circle error-code mapping is carried
  from source, not independently reconfirmed this pass.
- **04**: `LedgerPostingService`'s exact shape was undocumented in source material and had to be
  synthesized — flagged for review, not treated as settled. `GetCurrentBalanceQueryHandler`'s
  `Money.Zero("USD")` default vs. a funded account's `USDC` currency is an open product question.
  **Ticketed**: Phase 4 ticket 26 (`.scratch/treasury-service-orchestrator/PHASE4_IMPLEMENTATION_PLAN.md`).
- **06**: audit immutability is DB-enforced as of ticket 13 item 2 (trigger, `test-full.sh`
  fault-injection-covered); no retention implementation/ops record exists yet (deferred by
  decision, ticket 14); correlation id is not echoed back to the caller in the HTTP response.
  **Ticketed**: Phase 4 ticket 27.
- **13**: the Phase 1 plan's own integration test for outbox/state-change atomicity is
  happy-path only — no fault-injection test exists proving the same-transaction guarantee holds
  when `SaveChangesAsync` actually fails mid-way. **Ticketed**: Phase 4 ticket 28.
- **02.2 / CallerIdentityMiddleware**: PRD §2.2's "human portal user audit header" is still
  unimplemented (deferred by decision, ticket 14). ~~the middleware also has no bypass-path
  mechanism yet~~ — **partially resolved**: a path-based bypass now exempts
  `/v1/webhooks/circle` from `ClientCompanyId` scoping (PRD §10 item 7); whether 13's stub
  receiver needs the same treatment is still open. **Ticketed**: Phase 4 ticket 29.
- **03 / 07 / 08 stale-note correction (2026-07-18)**: this table itself had drifted from shipped
  code on two points — the unhandled-topic-webhook fix (item 03) and `GetWireInstructionsAsync`
  (item 08) were already resolved but a stale note in `docs/features/03-webhook-processing.md`
  and this file's own prior wording claimed otherwise. Corrected in place above; the lesson is
  this table needs periodic Grep-verification against code, not just doc-to-doc consistency.

None of these are doc-drift — they're genuine implementation gaps found while authoring the
corrected docs, left open rather than silently resolved. Every item above now has a ticket
(Phase 3 for the Circle-integration items, Phase 4 for the remainder) — see
`.scratch/treasury-service-orchestrator/PHASE3_IMPLEMENTATION_PLAN.md` and
`PHASE4_IMPLEMENTATION_PLAN.md`.
