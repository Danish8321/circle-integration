# Phase 3 — Real Circle Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the mock-mode gateways from `Phase_1_Feature_Slices.md` with real Circle Mint HTTP calls, real SNS-based webhook delivery, and a production-ready secrets/cutover path, per `docs/PRD.md` §15.3. Nothing in this phase changes the product's API contract, domain model, or Application-layer handlers — every change is confined to `Infrastructure` (gateways, webhook verification, DI wiring, config) plus one new cross-cutting mapping (Task 7) that Application consumes.

**Preconditions:** Phase 1 (`Phase_1_Feature_Slices.md`, 14 tasks) complete and green — `ISubAccountGateway`, `IStablecoinGateway`, the webhook inbox/processor pipeline, and mock-mode production guard already exist. Phase 2 (`DepositReconciliationPLan.md`) complete — `IStablecoinGateway.ListRecentDepositsAsync` (per ADR 0006 — on the Ledger gateway, **not** `ISubAccountGateway`) already exists with a Circle stub returning `[]`.

**Architecture:** Same Clean/Onion layering. `CircleSubAccountGateway` and `CircleMintGateway` (both already stubbed in Phase 1/2 with fake-success or empty-list bodies) get real `HttpClient`-backed implementations behind the same port interfaces — Application and Domain are untouched. Real webhook delivery replaces the mock emitter at the transport edge only; the durable inbox → dedup → per-topic-processor pipeline built in Phase 1 Task 5 is reused unchanged — this holds because Phase 1's payload contracts are the real Circle/SNS shapes (envelope, string-amount money objects, SNS `Message` unwrapping), per `docs/adr/0007-mock-emits-real-provider-shapes.md`. A new `CircleErrorTranslator` (Task 7) is the only new Application-visible surface, closing the gap identified in the doc-grilling pass: PRD §11.2's coarse error taxonomy (`validation`/`not-found`/`conflict`/`provider-rejected`/`provider-unavailable`) currently has no defined mapping from Circle's actual numeric error codes.

**Tech Stack:** .NET 10, `HttpClientFactory` + Polly (timeout/retry/circuit-breaker per PRD §11.3), EF Core, xUnit v3, no new NuGet packages beyond `Microsoft.Extensions.Http.Polly` (already implied by PRD §11.3 — confirm it isn't already referenced before adding).

## Module Boundaries

Per `Phase_1_Feature_Slices.md`'s Module Boundaries (decided 2026-07-16): `CircleSubAccountGateway`/entity-registration/wire-instruction concerns belong under `Compliance`, `CircleMintGateway`/transfer/redemption/balance concerns under `Ledger`, `CircleErrorTranslator` and SNS webhook verification under `Webhooks` — but this is an *Application/Domain* namespace split (ADR 0001), not an Infrastructure one. `Infrastructure` stays flat, no per-module subfolder (confirmed against `.claude/rules`'s tier table and Task 0's actual scaffold). File paths below written as `Infrastructure/Circle/*.cs` mean `Infrastructure/Providers/Circle/*.cs` — the flat convention Task 0 already established and Phase 1's `CircleSubAccountGateway.cs` already lives at (corrected 2026-07-17, doc-grilling).

## Global Constraints

- Same as `Phase_1_Feature_Slices.md` (net10.0, `TreatWarningsAsErrors`, no MediatR, `Money` boundary type, two-`SaveChangesAsync` idempotency pattern, `dotnet build`/`dotnet test` green after every commit).
- **No sandbox/production URL, API key, or certificate ever hardcoded** — all come from configuration/secret store (PRD §12, §14).
- `MockModeGuard` (Phase 1 Task 6) stays the single source of truth for "is mock mode allowed here" — this phase's production cutover (Task 11) verifies it, never bypasses it.
- Circle's `idempotencyKey` is a **request-body field** (UUID v4), confirmed from Circle's own Postman collections (`Core Functionality.postman_collection.json`), not an HTTP header — every real gateway call that mutates state must set it from the caller-supplied idempotency key already threaded through Phase 1's handlers.
- Circle Mint webhooks are **v1 / SNS-based**. The v2 ECDSA `X-Circle-Signature` scheme (Circle's own `verify-webhook-signatures.md`) explicitly does **not** apply and Circle's docs say v1 verification details require contacting a Circle rep — this plan implements verification against the **published, product-agnostic AWS SNS message-signing algorithm** (canonical string + cert from `SigningCertURL`, validated against the `sns.*.amazonaws.com` domain), not a Circle-specific scheme.
- Sandbox base URL `https://api-sandbox.circle.com`; production `https://api.circle.com` (per `sandbox-to-production.md`) — both configuration-driven, swapped per environment, never per-request.

---

## Task 1: Circle HTTP client foundation

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleClientOptions.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleHttpClientRegistration.cs` (or extend existing `Program.cs` DI section directly — check whether Phase 1 already created a `Circle/` DI-registration convention before adding a new file)
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json` (add `Circle` section: `BaseUrl`, `ApiKeySecretName`, timeouts)
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`

**Scope:**
- `CircleClientOptions { string BaseUrl; int TimeoutSeconds = 10; int RetryCount = 3; int CircuitBreakerFailureThreshold = 5; TimeSpan CircuitBreakerDurationOfBreak; }`.
- Register a named `HttpClient` ("Circle") via `IHttpClientFactory`, base address from `CircleClientOptions.BaseUrl`, `Authorization: Bearer {apiKey}` header set from the secret store (Task 6 wires the real secret; this task can start with a placeholder `IOptions<CircleClientOptions>`-bound key for sandbox use and a `// TODO(Task 6)` marker, since Task 6 replaces the source, not the header-setting code).
- Add a Polly policy handler: timeout (`TimeoutSeconds`), retry with exponential backoff on `provider-unavailable`-shaped failures (5xx, timeout, `HttpRequestException`) — **not** on 4xx, since those are `provider-rejected` and retrying would resubmit a bad idempotency key — and a circuit breaker (`CircuitBreakerFailureThreshold`/`CircuitBreakerDurationOfBreak`), matching PRD §11.3 exactly.
- [ ] Write `CircleClientOptionsTests` asserting the options bind correctly from config and default values match this spec.
- [ ] Wire the named client + Polly pipeline in `Program.cs`; confirm with an integration test that a `Circle`-named client resolves and its base address matches config.
- [ ] `dotnet build` clean, commit.

---

## Task 2: `CircleSubAccountGateway` — real implementation

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs` (replaces Phase 1's fake-success stub bodies one method at a time)
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/CircleSubAccountGatewayTests.cs` (recorded-response tests — see note below)

**Provider mapping (PRD Appendix B):**

| Method | Circle call | Body/query |
|---|---|---|
| `CreateExternalEntityAsync` | `POST /v1/externalEntities` | body: business details only — **no `idempotencyKey` field exists on this endpoint** (verified 2026-07-17); provider-side dedup is the 409 on duplicate `businessUniqueIdentifier`+country, local idempotency stays the two-`SaveChangesAsync` pattern |
| `GetExternalEntityAsync` | `GET /v1/externalEntities/{walletId}` | path |
| `GenerateDepositAddressAsync` | `POST /v1/businessAccount/wallets/addresses/deposit` | body: `idempotencyKey`, `currency`, `chain`; **`walletId` is a query parameter, not body** (verified 2026-07-17) — and omitting it targets the Master Account wallet, so the gateway must always set it (same hazard as `source` on transfers/payouts) |
| `RegisterRecipientAsync` | `POST /v1/businessAccount/wallets/addresses/recipient` | body: `idempotencyKey`, `address`, `chain`, `currency`, `description` |

> `ListRecentDepositsAsync` moved to Task 3 (2026-07-17) — it lives on `IStablecoinGateway`/`CircleMintGateway` per ADR 0006, not on this gateway.

- [ ] For each method: write a test against a recorded/fixture HTTP response (use `HttpMessageHandler` test double returning canned JSON from Circle's documented response shapes — do **not** call live sandbox in unit/integration tests; live-sandbox calls are Task 10 only) before implementing.
- [ ] Implement using the Task 1 named client. Map Circle's response fields to the existing `CreateExternalEntityResult`/`ExternalEntityStatusResult`/`GenerateDepositAddressResult`/`ProviderDepositRecord`/`RegisterRecipientGatewayResult` DTOs already defined in Phase 1/2 — no DTO shape changes, this task only changes what populates them.
- [ ] Any HTTP error maps through `CircleErrorTranslator` (Task 7) before the gateway throws — gateways never leak a raw `HttpResponseMessage` or Circle JSON body past this boundary.
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 3: `CircleMintGateway` (`IStablecoinGateway`) — real implementation

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs`
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/CircleMintGatewayTests.cs`

**Provider mapping:**

| Method | Circle call | Notes |
|---|---|---|
| `CreateLinkedBankAccountAsync` | `POST /v1/businessAccount/banks/wires` | body: `idempotencyKey` + bank details |
| `RedeemAsync` | `POST /v1/businessAccount/payouts` | body: `idempotencyKey`, `source: {type:"wallet", id}`, `destination: {type:"wire", id: bankId}`; response carries gross `amount` + `fees`, and **`toAmount` is optional** (verified 2026-07-17 — present on FX/institutional cases, not guaranteed): net = `toAmount` when present, else computed `amount − fees`, per PRD §8 |
| `CreateTransferAsync` | `POST /v1/businessAccount/transfers` | body: `idempotencyKey`, `source: {type:"wallet", id}`, `destination: {type:"verified_blockchain", addressId: <recipient UUID>}` (verified 2026-07-17 — literal is `verified_blockchain`, field is `addressId`) — **do not** add `identities`/originator fields (PRD §7.3, confirmed no such request field exists) |
| `GetTransferStatusAsync` | `GET /v1/businessAccount/transfers/{id}` | provider may report `running` between `pending` and `complete` — map `running` → `Pending` (PRD §7.2) |
| `ListRecentDepositsAsync` | `GET /v1/businessAccount/deposits?walletId=…` **and** `GET /v1/businessAccount/transfers?destinationWalletId=…` | two calls merged (moved from Task 2, 2026-07-17): deposits endpoint is fiat-wire-only, on-chain deposits arrive as incoming transfers; map to `ProviderDepositRecord.SourceType` `Wire`/`OnChain` respectively — replaces Phase 2's `=> []` stub |

**`source` is mandatory on every transfer/payout request this gateway builds** (verified 2026-07-17): Circle treats `source` as optional and defaults to the Distributor's Master Account wallet — an omitted `source` silently spends Distributor funds. Add a test per money-moving method asserting the outbound JSON always carries `source.id` equal to the sub-account wallet id passed in.

- [ ] Same fixture-first testing approach as Task 2.
- [ ] Redemption fee handling: two tests — (a) `toAmount` present and `!= amount`: all three fields flow into the ledger DTO untouched; (b) `toAmount` **absent**: net computed as `amount − fees` (PRD §8, corrected 2026-07-17).
- [ ] Wire instructions read endpoints (`GET /v1/businessAccount/banks/wires/{id}/instructions`, with/without `walletId`) — implement as part of this task since they share `CircleMintGateway`; confirm which port (`ISubAccountGateway` vs `IStablecoinGateway`) Phase 1 actually placed them on before adding (`Grep` both interfaces for `WireInstructions` first).
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 4: Idempotency-key forwarding audit

**Files:**
- No new files — audit/fix pass across Task 2 and Task 3's request-building code.

**Scope:** PRD §11.1 requires the caller-supplied idempotency key to be forwarded to Circle on every money-moving call, so a crash between our commit and the Circle call can't double-spend. Since Circle's field is `idempotencyKey` in the body (Task 1/confirmed constraint), verify each of these call sites sets it from the **same** key already reserved in the handler's `(ClientCompanyId, IdempotencyKey)` unique-index row (Phase 1's two-`SaveChangesAsync` pattern) — not a freshly generated GUID per HTTP attempt, which would defeat the point on retry.

- [ ] Write a test per money-moving gateway method (`CreateTransferAsync`, `RedeemAsync`, `GenerateDepositAddressAsync`, `RegisterRecipientAsync`, `CreateLinkedBankAccountAsync`) asserting the outbound JSON body's `idempotencyKey` equals the value passed into the gateway request DTO, not a value generated inside the gateway.
- [ ] Fix any gateway method found generating its own key instead of forwarding the caller's.
- [ ] `dotnet test`, commit.

---

## Task 5: SNS v1 webhook subscription + handshake

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleWebhookSubscriptionService.cs`
- Create: `src/TreasuryServiceOrchestrator.Api/Controllers/CircleWebhookSubscriptionConfirmationController.cs` (or extend the existing webhook-inbox controller from Phase 1 Task 5 — check first)
- Test: `tests/TreasuryServiceOrchestrator.IntegrationTests/CircleWebhookSubscriptionTests.cs`

**Scope:**
- Register the subscription once via `POST /v1/notifications/subscriptions` with `{ endpoint }` — v1 has no topic filter; it delivers all account events, so topic filtering (`externalEntities`, `deposits`, `transfers`, `payouts`, `addressBookRecipients`, `wire` — added 2026-07-17 per PRD §10) stays application-side in the Phase 1 per-topic dispatcher; unhandled topics (e.g. `paymentIntents`, credit topics) are stored + acknowledged, never errors.
- Handle the one-time `SubscriptionConfirmation` POST: the SNS message has `Type: SubscriptionConfirmation` and a `SubscribeURL` — confirm by issuing a `GET` to that URL (server-side, not by requiring a human to click it) before the subscription starts delivering real `Notification` messages.
- Respect the subscription cap: **Production allows only 1 active subscription** (Sandbox allows 3) — the registration step must be idempotent (check `GET /v1/notifications/subscriptions` for an existing confirmed subscription to this endpoint before creating a new one) or production deploys will collide with this limit on redeploy.
- [ ] Write the failing test for "no existing subscription → create one" and "existing confirmed subscription → skip creation".
- [ ] Implement, run against a fixture (not live) SNS response shape.
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 6: AWS SNS signature verification (replaces mock verifier)

**Files:**
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Webhooks/*SignatureVerifier*.cs` (Phase 1 Task 5 already has a verifier abstraction behind mock signatures — locate it with `Grep -rn "SignatureVerif" src/` before creating a new file)
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Webhooks/SnsSignatureVerifierTests.cs`

**Scope:** Circle's v1 scheme is raw Amazon SNS delivery — verification is the **standard, publicly documented AWS SNS message-signing algorithm**, not anything Circle-specific:
1. Build the canonical string from the SNS message fields in AWS's fixed field order (differs slightly for `Notification` vs `SubscriptionConfirmation`/`UnsubscribeConfirmation` message types — both must be supported).
2. Fetch the X.509 cert from `SigningCertURL`, **validate the URL's host matches the expected AWS SNS certificate domain pattern** (`sns.<region>.amazonaws.com`) before fetching — this is the step that prevents a forged `SigningCertURL` from defeating verification entirely; a naive implementation that trusts any `SigningCertURL` is not real verification.
3. Verify the base64 `Signature` against the canonical string using the cert's public key (SHA1withRSA for `SignatureVersion: "1"`, SHA256withRSA for `"2"` — support both, since AWS allows either).
4. Cache the fetched cert by `SigningCertURL` to avoid a network round-trip per webhook (mirrors the pattern Circle's own v2 doc recommends for public-key caching).
- [ ] Unit tests using a self-signed test certificate and a hand-built canonical message + signature (do not depend on network access in unit tests).
- [ ] Wire this verifier into the same "reject unverifiable, log it" path Phase 1 Task 5 already built for mock signatures — swap the implementation only, config-switched by environment (mock mode still uses the old mock verifier).
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 7: Circle error-code → PRD taxonomy mapping (`CircleErrorTranslator`)

**Files:**
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleErrorTranslator.cs`
- Test: `tests/TreasuryServiceOrchestrator.UnitTests/Infrastructure/Providers/Circle/CircleErrorTranslatorTests.cs`

**Why this task exists:** identified during doc-grilling — PRD §11.2 defines `validation | not-found | tenant-forbidden | conflict | provider-rejected | provider-unavailable`, but no doc maps Circle's actual synchronous codes (`error-codes.md`) onto it. Phase 1's mock gateway invents its own errors, so this was invisible until real HTTP calls arrive.

**Mapping table (from `docs/circle-mint-docs/reference/error-codes.md`):**

| Circle signal | → Product class |
|---|---|
| HTTP `400` + `errors[]` validation array | `validation` |
| HTTP `401`, code `1` (malformed auth) | `provider-unavailable` (config/secret problem, not caller's fault — alert, don't 400 the caller) |
| HTTP `403`, code `3` | `provider-unavailable` (decided 2026-07-17: entitlement/config problem on *our* key, same class as 401 — alert ops, never blame the tenant) |
| HTTP `429` (rate limited) | `provider-unavailable` (safe-to-retry; Task 1's retry policy must treat 429 as retryable alongside 5xx — added 2026-07-17) |
| HTTP `404` / code `5001`/`5004`/`5005` (resource not found variants) | `not-found` |
| HTTP `409`, code `1083` (idempotency key bound to another request), `409` external-entity duplicate | `conflict` |
| Codes Circle documents as "State conflict": `1084`-`1086`, `1097`, `1107`, `2003` (recipient already registered), `2004`, `5003` (destination in 24h inactive hold) — regardless of HTTP status | `conflict` (added 2026-07-17) |
| Code `1093`/`5006`/`insufficient_funds` (sync or async) | `provider-rejected` (surface as a specific "insufficient funds" reason, not generic) |
| Async `errorCode: transaction_denied`/`transfer_denied` + `riskEvaluation.decision: denied` | `provider-rejected` (compliance/risk denial — do not auto-retry; needs a corrected resubmission) |
| Generic `code: -1`, HTTP `5xx`, timeout | `provider-unavailable` (safe-to-retry) |

- [ ] Write the test table above as parameterized unit tests: given a synchronous HTTP status + Circle `code`, or an async `errorCode`/`riskEvaluation`, assert the correct PRD error class comes out.
- [ ] Implement `CircleErrorTranslator.Translate(...)` returning the existing PRD §11.2 error type (check Phase 1 Task 2's actual exception/result type name — `ProviderRejectedException` or similar — before inventing a new one).
- [ ] Wire into Task 2/3's gateways at every HTTP-error branch.
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 8: Secrets — Circle API key in managed secret store with rotation

**Files:**
- Create/modify: wherever Phase 1 already established secret-store access (check `Grep -rn "SecretClient\|IConfiguration.*Secret\|KeyVault"` across `src/` first — do not introduce a second secrets mechanism if one already exists for another credential).
- Modify: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleHttpClientRegistration.cs` (Task 1) — replace the placeholder key source.

**Scope:**
- Circle API keys have no enforced rotation cadence *unless* mTLS is enabled (see Task 9 decision) — in which case keys expire at 180 days and must be rotated before then (`rotate-mtls-api-key.md`). Either way, PRD §12/§14 requires the key live in a managed secret store, never in config files, with rotation support.
- [ ] Store the sandbox and production Circle API keys as separate secrets (`Circle:ApiKey:Sandbox`, `Circle:ApiKey:Production` or equivalent), resolved by environment at startup, never both loaded into the same process.
- [ ] Add a startup health check that fails fast (not silently falls back to mock) if the expected secret is missing in a non-mock environment.
- [x] ~~If mTLS is adopted (Task 9), add a scheduled reminder/alert at day 150 of the 180-day key lifetime~~ — dropped: Task 9 decided against mTLS (2026-07-17), so no enforced key lifetime applies.
- [ ] Integration test: startup fails clearly when the secret is absent; succeeds when present (using a fake secret provider in tests, not a real vault).
- [ ] `dotnet build`/`dotnet test`, commit.

---

## Task 9: mTLS decision and (if adopted) client-certificate wiring

**Files:** TBD, contingent on the decision below.

**Open decision — needs a human call, not an inferred default:** the org's PRD (§1.1) describes a Circle Mint **(US)** account. mTLS is mandatory only for entities regulated under EU/EEA MiCA; for a US-only entity it is purely an **optional** extra security layer (`mtls-authentication-overview.md`). Two paths:

- **Skip mTLS** (default posture for a US-only entity with no MiCA exposure): this task becomes a no-op; document the decision inline in this file and close it.
- **Adopt mTLS anyway** (defense-in-depth): requires generating an ECDSA P-256 key pair + CSR (never leaves our environment), submitting it to Circle, configuring the resulting client cert on the `HttpClient` used in Task 1, switching the API key regeneration process to Mint Console + MFA, and accepting the mandatory 180-day key lifetime (feeds Task 8's rotation reminder). This also revokes **all existing API keys** the moment it's enabled on the entity — must be scheduled as a deliberate cutover, not toggled casually.

- [x] **Decided 2026-07-17 (doc-grilling): skip mTLS.** US-only entity, no MiCA exposure — mTLS is optional here, and enabling it revokes all existing API keys and imposes a 180-day key lifetime for no compliance requirement we have. Task 9 is a no-op; Task 8's day-150 rotation reminder bullet drops out with it. Revisit only if the org gains EU/EEA MiCA-regulated status.

---

## Task 10: Sandbox end-to-end run

**Files:**
- New test target/script only — no product code changes. Reuses Phase 1 Task 14's `DemoScriptEndToEndTests` flow but pointed at real Circle sandbox instead of mock mode.

**Scope:** Execute the PRD §15.1 demo script (admin creates sub-account → entity screening `ACCEPTED`/`REJECTED`+resubmit → deposit address → real sandbox deposit → recipient registration → **real Mint Console human approval** (out-of-band, per PRD §7.1 — this is the one step that cannot be automated) → transfer → redemption with gross/fees/net) against `https://api-sandbox.circle.com` with mock mode switched off.
- [ ] Stand up a sandbox-pointed deployment with real sandbox API key (Task 8) and, if adopted, sandbox client cert (Task 9).
- [ ] Run the flow manually once, pausing at the recipient-approval step for a human to approve in the sandbox Mint Console.
- [ ] Confirm every assertion from Phase 1's `DemoScriptEndToEndTests` DTO shapes still holds against real Circle response shapes — fix any field-name drift found (Circle's real JSON vs the shapes assumed in Tasks 2/3) in the gateway mapping code, not by loosening the test.
- [ ] Record settlement-time and fee observations (`sandbox-to-production.md` flags these differ from sandbox defaults) for the production runbook.
- [ ] Commit any gateway/mapping fixes found during this run as their own small commits, not squashed into "sandbox run".

---

## Task 11: Production cutover checklist

**Files:**
- Create: `docs/Production_Cutover_Checklist.md`
- No product code — this task is the go-live gate, per `sandbox-to-production.md` + PRD §15.3.

**Checklist to author and then execute:**
- [ ] Swap `Circle:BaseUrl` from `api-sandbox.circle.com` to `api.circle.com` via config, never code.
- [ ] Swap the API key secret reference to the production key (Task 8); confirm the sandbox key is not reachable from the production environment.
- [ ] (Optional) configure Circle's IPv4 allowlist to the production environment's egress IPs.
- [ ] Verify API key role/scope in production — some sandbox-available endpoints may 403 in production until Circle enables the entitlement (`error-codes.md`'s Institutional Distribution `401` case); test each Appendix B endpoint once against production before declaring done.
- [ ] Re-verify `MockModeGuard` (Phase 1 Task 6) actually throws in the production environment — this is the safety net the whole plan depends on; test it against the real `Environments.Production` value in the deployed config, not just unit tests.
- [ ] Confirm the deposit-reconciliation background job (Phase 2) is running against production before any real client money moves (PRD §15.2 explicitly gates this).
- [ ] Confirm the SNS subscription (Task 5) is registered and confirmed against the production endpoint, respecting the 1-subscription production cap.
- [ ] Re-pull the current supported-chains/currencies list from `https://developers.circle.com/llms.txt` (per `supported-chains-and-currencies.md`'s own caveat that its local copy is a summary, not authoritative) and diff against whatever chain/currency values Task 2/3's deposit-address and transfer code paths assume — fix any drift before go-live.
- [ ] Anticipate production transaction fees differ from sandbox (`sandbox-to-production.md`) — confirm the Task 3 redemption fee-recording path (gross/fees/net) handles real fee values, not just the sandbox test fixture's numbers.
- [ ] Sign-off: run Task 10's demo script one more time against production with the smallest possible real amounts, before declaring Phase 3 complete.
