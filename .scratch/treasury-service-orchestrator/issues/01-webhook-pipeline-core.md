Status: resolved (Compliance-topic scope only — other topics still unbuilt)

Source: `docs/features/03-webhook-processing.md`, `docs/README.md` §7. (Old source
`docs/Phase_1_Feature_Slices.md` Task 5 was deleted this session — superseded by the
per-feature doc restructure.)
Blocked by: none.

## Correction to original scope (ticket premise was stale)

This ticket was filed against a hypothetical prior state ("`WebhookInboxEntry` exists in schema
today", "`CircleWebhooksController` parses the SNS envelope and calls
`ProcessExternalEntityDecisionHandler` directly") that did not match reality: a full `find src`
audit this session found **no** `Application.Webhooks` module, no controller, no inbox — the
whole pipeline was unbuilt. The file list below is what was actually built, not the ticket's
original guess (topic processors landed in `Infrastructure/Providers/Circle/`, not
`Application/Webhooks/` — they parse Circle-specific wire shapes, which is Infrastructure's job
per the tier rules, not Application's).

## What's done

- `Application/Webhooks/WebhookInboxEntry.cs`, `WebhookProcessingStatus.cs` (**3-way**:
  `Processed | Failed | Unhandled` — the ack-don't-retry fix for unhandled topics was built in
  from the start, not retrofitted), `IWebhookTopicProcessor.cs`, `IncomingWebhookEvent.cs`,
  `WebhookProcessor.cs` (dispatcher), `Ports/IWebhookInboxRepository.cs`,
  `Ports/ISnsSignatureVerifier.cs`.
- `Infrastructure/Persistence/WebhookInboxRepository.cs` (EF-backed, dedup via unique-index
  `DbUpdateException` catch on `CircleEventId`), `Infrastructure/Webhooks/
  MockSnsSignatureVerifier.cs` (Phase-1 always-valid stand-in), `Infrastructure/Providers/Circle/
  ExternalEntitiesWebhookTopicProcessor.cs` + `ExternalEntityWebhookEnvelope.cs`.
- `Api/Webhooks/CircleWebhooksController.cs` (verifies via mock, unwraps SNS `Message`,
  dispatches, maps `Processed|Unhandled → 200`, `Failed → 500`), `CallerIdentityMiddleware`
  bypass for `/v1/webhooks/circle`, `Program.cs` DI wiring.
- `TreasuryServiceOrchestratorDbContext` + first-ever migration `20260717153028_InitialCreate`
  (hand-reviewed: clean creates only, no drops — this was the *first* migration for this
  `DbContext`, nothing to preserve). **Not applied to a database yet.**
- `WebhookProcessorTests` (Moq, 4 tests): dedup, happy path, unhandled-topic → `Unhandled`
  status + ack, processor-throws → `Failed`. Green under `test-fast.sh` (77/77 total suite).
- `check.sh` green on `Api` project (build + analyzers, warnings-as-errors).

## Resolved this pass (2026-07-17, grilling session)

- `WebhookDedupTests` integration test added (`tests/.../Webhooks/WebhookDedupTests.cs`,
  Testcontainers real SQL Server): redelivered `MessageId` produces exactly one
  `WebhookInboxEntry` row; `ACCEPTED` decision activates the seeded `SubAccount`; unknown topic
  returns 200 not 500; `/v1/webhooks/circle` confirmed reachable with no `ClientCompanyId`
  header (middleware bypass). `test-full.sh` run: **29/29 green**.
- Dead-letter threshold decided during grilling: count-based, `Attempts >= 5`
  (`WebhookDeadLetterPolicy.AttemptThreshold`, `Application/Webhooks/WebhookDeadLetterPolicy.cs`).
  Alerting hook: `WebhookInboxRepository.MarkFailedAsync` logs a structured warning
  (`LoggerMessage`-generated) once an entry crosses the threshold — no external alerting
  pipe (PagerDuty/etc.) wired, since none exists in this repo yet; the log is the hook.
  Unit-tested (`WebhookDeadLetterPolicyTests`).
- Glossary term reconciled: `CONTEXT.md` renamed "WebhookEvent" → "WebhookInboxEntry" to match
  shipped code, and added the "Dead-lettered" term.

## Still open (real gaps, decided-and-deferred, not doc drift)

- Replay endpoint: explicitly **deferred** during grilling — no `Admin` module exists yet
  (confirmed empty in `src/`), and replay is B0.5's natural Admin-module job. Building a
  one-off endpoint elsewhere was rejected as premature.
- `SubscriptionConfirmation` handshake: explicitly **left as-is** during grilling — the
  `SubscribeURL` server-side `GET` is untestable against Phase 1 mock mode (no real SNS
  subscription exists until Phase 3's real Circle sandbox); automating it now would be
  dead code. Controller still acknowledges (200) the handshake message without following it.
- `ISnsSignatureVerifier` is mock-only; real AWS SNS cert/signature verification remains
  Phase 3 scope per docs/features/03 §4 — not a regression, just not done.
- Migration `20260717153028_InitialCreate` generated and hand-reviewed, still not applied to
  any database (`schema.sh apply` not requested yet).

## Definition of done (updated)

- [x] `WebhookProcessorTests` (Moq-based) green: dedup, Processed/Failed/Unhandled, unknown
      topic doesn't throw and doesn't dead-letter.
- [x] `WebhookDedupTests` integration test (Testcontainers) — duplicate `CircleEventId`
      delivery is a no-op second time.
- [x] `check.sh` green (Api project).
- [x] `test-fast.sh` green (80/80).
- [x] `test-full.sh` green (29/29).
- [x] Migration reviewed by hand.
- [ ] Migration applied (`schema.sh apply`) — not requested yet.
- [ ] Dead-letter alerting wired to a real external channel — no such channel exists in this
      repo yet; structured log is the current hook.

## Comments
