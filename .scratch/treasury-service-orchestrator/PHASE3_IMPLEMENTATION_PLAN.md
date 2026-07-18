# Phase 3 Implementation Plan

Status: draft, awaiting approval. Phase 1 (tickets 02-14) and Phase 2 (tickets 15-20) fully
shipped and closed. Goal per `docs/README.md` line ~102: "Real Circle integration: sandbox ->
production. Real HTTP gateway, SNS webhook signature verification, secrets in managed store,
sandbox demo-script run with a real Mint Console approval, production cutover checklist."

## Scoping decisions (2026-07-18 grilling)

- **"Real HTTP gateway" is largely already shipped**, not a from-zero Phase 3 item.
  `CircleMintGateway`/`CircleSubAccountGateway` (Phase 1) and Polly resilience (ticket 17, Phase 2)
  are real, wired, unit/integration-tested against fakes/Testcontainers — but never exercised
  against an actual live Circle sandbox account. User chose to still ticket this as a verification
  pass (ticket 21), not skip it.
- **Secrets store: AWS Secrets Manager** (not a self-hosted vault, not Azure Key Vault) — see
  `docs/adr/0009-secrets-in-aws-secrets-manager.md`. This repo's webhook transport is already
  AWS-SNS-based (Circle's own choice, not ours), so this keeps one cloud footprint instead of
  introducing a second (Azure) or a new self-hosted ops burden (Vault OSS) on top of ADR 0008's
  self-hosted SQL Server.
- **Sandbox demo-script run is external/manual** — no Circle sandbox credentials available in this
  session. Scoped as code-readiness (secrets plumbing for real credentials) + a runbook a human
  follows in Circle's sandbox Mint Console, not an automated task.
- **Correction (2026-07-18 audit)**: `docs/features/03-webhook-processing.md` §7 item 7 claimed
  the unhandled-topic-webhook defect (`Failed` -> HTTP 500 -> SNS redelivers forever) was still
  unresolved. Cross-checked against `docs/README.md`'s own "Known open items" table and shipped
  code (`WebhookProcessingStatus.Unhandled`, mapped to HTTP 200 in `CircleWebhooksController`):
  **already fixed**, that feature-doc note is stale. Ticket 22.4 (below) is dropped as redundant.

Order: **21 (gateway sandbox verification) -> 22 (real SNS verifier + unhandled-topic fix) -> 23
(AWS Secrets Manager) -> 24 (sandbox demo-script readiness) -> 25 (production cutover
checklist)**. 21 first since it's the cheapest signal on whether the existing gateway code
actually works against real Circle, before investing in secrets/cutover plumbing around it.

Same verification contract as Phase 1/2: no task is "done" without its stated verification
passing. Migrations always hand-reviewed and shown to the user before `schema.sh apply`.

---

## Ticket 21 — Real Circle gateway sandbox verification

Not new code by default — a verification pass proving the existing `CircleMintGateway`/
`CircleSubAccountGateway` actually work against a live Circle sandbox account, not just against
Testcontainers/fakes. Blocked by: **real Circle sandbox credentials**, which this session does not
have. This ticket's own first sub-task is establishing whether/when those become available.

### 21.1 — Credential readiness check
- No files changed. Confirm with the user whether Circle sandbox API credentials exist or need to
  be requested (Circle Mint sandbox signup), and where they'll be supplied (env var, local
  secrets file — NOT committed to the repo).
- Verify: no script — this is a go/no-go gate for the rest of ticket 21.

### 21.2 — Manual sandbox smoke run
- Files: none (or a throwaway local `appsettings.Sandbox.json`, git-ignored, not committed).
- Change: point `CircleClientOptions.BaseUrl` at Circle's sandbox host, run the existing demo
  script (ticket 10.1) against it with mock mode OFF, exercise: generate deposit address, register
  recipient, create transfer, redeem, get wire instructions, get main wallet balance.
- Verify: manual — a human confirms each call succeeds against real sandbox responses, notes any
  shape mismatch against this repo's DTOs (fix as its own sub-task, 21.3, if any surface).

### 21.3 — Fix any DTO/behavior mismatches found
- Files: whichever `Infrastructure/Providers/Circle/*.cs` DTOs or gateway methods 21.2 flags.
- Verify: `test-fast.sh` + `test-full.sh` (regression-free) plus a repeat of the specific 21.2 call
  that surfaced the mismatch.

---

## Ticket 22 — Real SNS signature verification + unhandled-topic fix

Design fully spec'd already in `docs/features/03-webhook-processing.md` §3 — no further grilling
needed. Blocked by: none (verification logic needs no live SNS subscription, only a real/
self-signed cert per the doc's own test strategy).

### 22.1 — Real `SnsSignatureVerifier`
- Files: `Infrastructure/Webhooks/SnsSignatureVerifier.cs` (replaces `MockSnsSignatureVerifier` in
  Production DI registration only — mock stays for mock-mode/dev per CLAUDE.md invariant 9).
- Change: implement §3.4 exactly — canonical-string construction (different field order/set for
  `Notification` vs `SubscriptionConfirmation`/`UnsubscribeConfirmation`), cert-domain validation
  (`SigningCertURL` host must match `sns.<region>.amazonaws.com` BEFORE fetching — §3.4 item 2's
  security-critical check), fetch cert, verify signature (`SignatureVersion` 1 = SHA1, 2 = SHA256).
- Verify: `SnsSignatureVerifierTests` — hand-built canonical message + signature against a
  self-signed test certificate, no network access. Cover both message-type field orders, both
  `SignatureVersion` values, and the cert-domain-validation rejection path (forged `SigningCertURL`
  host -> rejected without a fetch attempt) -> `test-fast.sh Infrastructure`.

### 22.2 — Rejection path wiring
- Files: `Api/Webhooks/CircleWebhooksController.cs`.
- Change: verification failure (bad signature, cert domain mismatch, cert fetch failure,
  unparseable envelope) returns HTTP 403, logs the rejection, no `WebhookInboxEntry` row written
  (§3.5 — PRD §10 item 2's "rejected and logged," not stored).
- Verify: Api integration test — unverifiable signature -> 403, no inbox row -> `test-full.sh`.

### 22.3 — `SubscriptionConfirmation` handshake
- Files: `Infrastructure/Providers/Circle/CircleWebhookSubscriptionService.cs` (or wherever the
  handshake fetch belongs per §3.3 step 2 — check existing file first).
- Change: on a verified `SubscriptionConfirmation` message, issue a server-side `GET` to
  `SubscribeURL` to complete the handshake; 200 response, no inbox row written (transport
  handshake, not a business event).
- Verify: Api integration test — `SubscriptionConfirmation` message -> handshake `GET` observed
  via test double, 200, no inbox row -> `test-full.sh`.

### 22.4 — DI wiring + config
- Files: `Api/Program.cs` (register real `SnsSignatureVerifier` in the non-mock-mode branch,
  mirroring how real Circle gateways are already split from mocks), `appsettings.json` (any new
  config, e.g. accepted SNS topic ARN allowlist if §3 calls for one — check the doc).
- Verify: `check.sh` full solution.

---

## Ticket 23 — Secrets in AWS Secrets Manager

Per `docs/adr/0009-secrets-in-aws-secrets-manager.md`. Blocked by: none for the code; actual AWS
account/Secrets Manager provisioning is an external step (like ADR 0008's SQL Server), the code
just needs to be able to consume it once provisioned.

### 23.1 — `ISecretProvider` port + AWS implementation
- Files: `Application/Shared/Ports/ISecretProvider.cs` (or reuse `IConfiguration` directly via the
  AWS Secrets Manager configuration-provider package — check whether this repo's existing options-
  binding pattern (`Configure<T>` from `IConfiguration`) already gets this "for free" once the AWS
  provider is added to the configuration builder in `Program.cs`, which would mean NO new port is
  needed, just a configuration-source registration — prefer this if it works, per CLAUDE.md's
  "simplest solution, existing patterns before new abstraction").
- Change: add `Amazon.Extensions.Configuration.SecretsManager` (or equivalent) as a configuration
  source in `Program.cs`, Production-environment-gated (mirrors CLAUDE.md invariant 9's mock-mode
  environment check — secrets-manager wiring should be structurally absent outside Production, not
  just unconfigured).
- Verify: `check.sh` full solution; a DI-resolution integration test asserting the configuration
  source is registered only when `ASPNETCORE_ENVIRONMENT=Production` -> `test-full.sh`.

### 23.2 — Migrate Circle credentials + SNS topic ARN to secrets-backed config
- Files: `appsettings.json` (remove any plaintext credential placeholders if present — check first;
  this repo may already keep these out of source), documentation of the exact secret names/paths
  Secrets Manager must contain (e.g. `treasury-orchestrator/circle-api-key`) in a new
  `docs/ops/secrets-management.md` (doc-only, mirrors ticket 13's `docs/ops/audit-retention.md`
  pattern for an ops runbook against a not-yet-provisioned target).
- Verify: `check.sh`; no functional test possible without a real AWS account — note this
  explicitly rather than fabricating one.

---

## Ticket 24 — Sandbox demo-script readiness (code + runbook, not an automated run)

External/manual per grilling decision — this repo has no sandbox credentials. Scoped as
readiness, not execution. Blocked by: ticket 21 (gateway must already be sandbox-clean) and ticket
23 (credentials should come from Secrets Manager, not a hardcoded sandbox config).

### 24.1 — Runbook
- Files: `docs/ops/sandbox-demo-script-runbook.md` (doc-only).
- Change: step-by-step instructions for a human: obtain Circle sandbox credentials, register a
  Circle Mint Console sandbox webhook subscription (ties to ticket 22.3's handshake code), run
  `demo-script` (ticket 10.1) with mock mode off against sandbox, what a real Mint Console approval
  step looks like for whichever flow requires manual approval in Circle's sandbox UI (check ticket
  10.1's demo script for which operation this applies to — e.g. redemption/payout approval).
- Verify: no script — doc-only, reviewed by the user like ticket 13's ops runbooks were.

---

## Ticket 25 — Production cutover checklist

Doc-only, scoped to this repo's actual deployment target (self-hosted SQL Server per ADR 0008,
AWS Secrets Manager per ADR 0009) — not a generic cloud-migration checklist. Blocked by: tickets
21-23 (checklist references their outputs).

### 25.1 — Checklist
- Files: `docs/ops/production-cutover-checklist.md` (doc-only).
- Change: concrete, ordered checklist covering: self-hosted SQL Server provisioned + migrations
  applied (`schema.sh verify`) + TDE enabled (ticket 13's `docs/ops/pii-at-rest-tde-plan.md`
  executed, not just documented) + retention backups configured (`docs/ops/audit-retention.md`
  executed); AWS Secrets Manager populated with production Circle credentials + SNS topic ARN;
  `MockModeGuard`'s structural Production check (CLAUDE.md invariant 9) verified live — mock mode
  cannot be enabled; real SNS subscription registered against the production webhook endpoint,
  handshake completed; Circle production API access approved/activated (external, Circle-side
  step); rollback plan if cutover fails.
- Verify: no script — doc-only, reviewed by the user.

---

## Execution protocol (per writing-plans skill)

Same as Phase 1/2: dispatch `task-executor` per sub-task, two-stage review (spec compliance
against the stated verification, then code-quality against CLAUDE.md invariants), only then mark
done and move to the next. Ticket 21 and 24 have hard external blockers (real credentials) this
session does not currently have — flag explicitly when reached rather than fabricating a pass.
Migrations shown to the user before `schema.sh apply` (none currently expected in this plan).
