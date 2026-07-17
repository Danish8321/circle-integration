# 03 — Webhook Processing (pipeline mechanics)

Owns the **generic** inbound-webhook pipeline: durable inbox, dedup, per-topic dispatch
framework, retry/dead-letter, replay, and the SNS v1 transport layer (subscription handshake +
signature verification) underneath it. Module: `Application.Webhooks` (ADR 0001).

**Not owned here** — topic-specific business logic (what an `externalEntities`/`deposits`/
`transfers`/`payouts`/`addressBookRecipients`/`wire` payload *means* and what handler it invokes)
lives in each feature's own file (07–13). This file only defines the shape every topic processor
plugs into. Internal outbound event notifications (the `NotificationOutboxEntry` callback to
legacy/portal consumers, PRD §10.1) are a separate, unrelated outbox — see
`13-internal-notifications-outbox.md`; do not conflate the two "inbox vs outbox" mechanisms.

---

## 1. Scope / PRD requirement

PRD §10 (Cross-cutting: Webhook Processing):

1. **Processed topics**: `externalEntities`, `deposits`, `transfers`, `payouts`,
   `addressBookRecipients`, `wire` (linked-bank-account verification lifecycle
   `pending → complete | failed`). The v1 subscription itself has **no topic filter** — Circle
   delivers *all* account events to the one endpoint; topic selection is application-side
   dispatch. Unhandled/unknown topics are **stored and acknowledged, not treated as errors**.
2. **Signature verification** on every delivery; unverifiable messages are rejected and logged.
3. **Deduplication** by provider event id — deliveries are at-least-once.
4. **Durable event store**: every delivery persisted with raw payload, verification result, and
   processing status before any side effect runs.
5. **Retryable processing**: a failed handler retries; events exhausting retries land in a
   **dead-letter state** with alerting.
6. **Replay**: an admin can re-run processing for a stored event (idempotent handlers make replay
   safe).
7. **Webhook endpoint authentication** is by signature verification, not by `ClientCompanyId` —
   the provider is not a registered caller, so `ICallerContext`/tenant scoping does not apply to
   this endpoint.

This file also covers the wire transport underneath the above: Circle Mint's notifications are
**v1 / SNS-based**, not the v2 direct-HTTPS ECDSA scheme used by Circle's other product lines.
See §3 for the live-verified detail.

---

## 2. Pipeline design

### 2.1 Durable inbox — `WebhookInboxEntry`

Every verified delivery is persisted **before** any side effect runs. Lives in `Application`
(not `Infrastructure`) so the port that manages it can be referenced without `Application`
depending on `Infrastructure`; EF Core maps POCOs from any referenced assembly, so this is a
plain Application-layer type, not an entity that must live alongside `DbContext`.

```csharp
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public sealed class WebhookInboxEntry
{
    public Guid Id { get; set; }
    public required string Topic { get; set; }
    public required string CircleEventId { get; set; }     // SNS MessageId — dedup key
    public required string PayloadJson { get; set; }        // unwrapped Circle envelope (see §2.5)
    public DateTime ReceivedAtUtc { get; set; }              // via TimeProvider, never DateTime.UtcNow directly
    public bool Processed { get; set; }
    public string? ProcessingResult { get; set; }            // "Processed" | "Failed"
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
```

Persistence: unique index on `CircleEventId` is the dedup mechanism (§2.3). `Topic` is bounded
(`nvarchar(64)`).

```csharp
public enum WebhookProcessingStatus { Processed, Failed }
```

### 2.2 `IWebhookTopicProcessor` — the dispatch seam

The one interface every topic-specific feature file (07–13) implements. This file defines it;
those files define implementations and register them via DI — no change to the dispatcher below
is needed to add a topic.

```csharp
namespace TreasuryServiceOrchestrator.Application.Webhooks;

public interface IWebhookTopicProcessor
{
    string Topic { get; }
    Task ProcessAsync(string payloadJson, CancellationToken cancellationToken);
}

public sealed record IncomingWebhookEvent(string Topic, string ProviderEventId, string PayloadJson);
```

`IWebhookInboxRepository` (`Application/Webhooks/Ports/`) is the port; `Infrastructure` supplies
the EF-backed implementation:

```csharp
public interface IWebhookInboxRepository
{
    Task<bool> TryAddAsync(WebhookInboxEntry entry, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken);
}
```

### 2.3 Deduplication

Dedup key is the provider's delivery id — SNS `MessageId` for v1 (reused verbatim by Circle on
redelivery). Mechanism: `IWebhookInboxRepository.TryAddAsync` attempts an insert against a
`CircleEventId`-unique index; a unique-constraint violation (SQL Server error `2601`/`2627`)
means "already seen" and the repository returns `false` without throwing. The dispatcher
(`WebhookProcessor`, §2.4) treats `false` as **already processed** and acknowledges without
re-invoking the topic processor — this is what makes at-least-once SNS delivery safe.

Dedup happens at insert time, *before* the topic processor runs, so a crash between insert and
processing still leaves a durable, re-checkable row (the row's `Processed=false` state is exactly
what makes replay (§2.6) possible).

### 2.4 `WebhookProcessor` — the dispatcher

```csharp
public sealed class WebhookProcessor(
    IWebhookInboxRepository inbox, IEnumerable<IWebhookTopicProcessor> topicProcessors)
{
    public async Task<WebhookProcessingStatus> HandleAsync(
        IncomingWebhookEvent incoming, CancellationToken cancellationToken = default)
    {
        var entry = new WebhookInboxEntry { /* Id, Topic, CircleEventId, PayloadJson, ReceivedAtUtc, Processed = false */ };

        if (!await inbox.TryAddAsync(entry, cancellationToken))
            return WebhookProcessingStatus.Processed; // already-seen event id; ack without reprocessing

        var topicProcessor = topicProcessors.FirstOrDefault(p =>
            string.Equals(p.Topic, incoming.Topic, StringComparison.Ordinal));

        if (topicProcessor is null)
        {
            // Unhandled topic (e.g. paymentIntents, credit topics): stored + acknowledged per PRD §10 item 1,
            // NOT dead-lettered — there is no processor to retry against. Mark Failed only in the sense of
            // "no processor ran"; the controller still returns 200 so SNS does not redeliver forever.
            // (See note below — this is the one branch where "Failed" status does not mean "retry".)
            await inbox.MarkFailedAsync(entry.Id, $"No processor registered for topic '{incoming.Topic}'.", cancellationToken);
            return WebhookProcessingStatus.Failed;
        }

        try
        {
            await topicProcessor.ProcessAsync(incoming.PayloadJson, cancellationToken);
            await inbox.MarkProcessedAsync(entry.Id, cancellationToken);
            return WebhookProcessingStatus.Processed;
        }
        catch (Exception ex)
        {
            await inbox.MarkFailedAsync(entry.Id, ex.Message, cancellationToken);
            return WebhookProcessingStatus.Failed;
        }
    }
}
```

**Correction against Phase_1 Task 5's literal code** (flagged during this write-up, not yet fixed
in shipped code — track as a defect against `WebhookProcessor`/`CircleWebhooksController`):
the "unknown topic" branch and the "processor threw" branch both currently return
`WebhookProcessingStatus.Failed`, and the controller maps *any* `Failed` to HTTP 500, which tells
SNS to redeliver. That is correct for a transient processing failure (retry is the point), but
**wrong for an unhandled topic** — PRD §10 item 1 explicitly says unhandled topics are
"stored and acknowledged, not errors," yet the literal code as transcribed in Phase_1 makes SNS
redeliver an unhandled-topic event forever since no processor will ever appear. The fix belongs
in code, not this doc: `WebhookProcessor` should distinguish "no processor" (ack, HTTP 200, no
dead-letter) from "processor threw" (retry path, HTTP 500, eventual dead-letter) — e.g. a third
`WebhookProcessingStatus.Unhandled` value that the controller maps to 200. Filed as an open
question in §7.

### 2.5 Envelope unwrapping — what `PayloadJson` contains

Per ADR 0007 (`docs/adr/0007-mock-emits-real-provider-shapes.md`), a delivery is an SNS
`Notification` whose top-level `Message` field is a **JSON string** containing Circle's own
envelope: `{ clientId, notificationType, version, customAttributes, <resourceKey>: {...} }`, with
nested money objects carrying **string** amounts (e.g. `{"amount": "1000.00", "currency": "USD"}`).

The controller/inbox layer unwraps one level (`envelope.Message` → the inner JSON string) before
handing `payloadJson` to `WebhookProcessor` / the topic processor. `WebhookInboxEntry.PayloadJson`
stores the **inner** Circle envelope (post-unwrap), not the outer SNS wrapper — the outer SNS
fields (`Type`, `MessageId`, `TopicArn`, `Signature`, `SigningCertURL`) are transport metadata,
consumed once by the controller/verifier and not persisted as part of the business payload.
`notificationType` inside the inner envelope is the dispatch key (`IncomingWebhookEvent.Topic`).

Per-topic processors (07–13) parse the real nested resource shape and convert string amounts to
`Money` at this boundary — `Money` is the only monetary type crossing into
Application/Domain (Global Constraint #10).

### 2.6 Retry, dead-letter, replay

- **Retry**: retry is provider-driven, not a local timer. A `Failed` processing outcome causes
  the controller to return HTTP 5xx, which is SNS's own at-least-once redelivery signal (SNS
  retries a failed subscriber delivery on a backoff schedule it controls). `WebhookInboxEntry`
  tracks `Attempts`/`LastError` across those redeliveries via `MarkFailedAsync`.
- **Dead-letter**: PRD §10 item 5 requires a dead-letter state with alerting once retries are
  exhausted. The inbox row already carries the data needed (`Attempts`, `LastError`,
  `ProcessingResult`); an explicit dead-letter flag/threshold and the alerting hook are **not yet
  specified with a concrete threshold** in Phase_1/Phase_3 — flagged as an open item (§7). SNS's
  own redelivery schedule (with an optional SNS-side DLQ) is the outer bound; this service's
  inbox is the inner, application-visible record.
- **Replay**: an admin-triggered re-run against a stored `WebhookInboxEntry` — re-invoke the
  matching `IWebhookTopicProcessor.ProcessAsync(entry.PayloadJson, ct)` for a given `Id`, bypassing
  the dedup check (replay is intentionally re-processing a known event). Safety depends entirely
  on every topic processor being idempotent on its own terms (each 07–13 file's responsibility —
  this file only guarantees the *pipeline* won't silently double-fire on ordinary redelivery).
  No dedicated replay port/endpoint exists yet in Phase_1/Phase_3 task lists — flagged as an open
  item (§7); the Admin module (per module boundaries, ADR "B0.5") is the natural home for the
  triggering endpoint.

---

## 3. SNS transport (Circle Mint v1)

### 3.1 Why v1, not v2 — verified live 2026-07-17

Circle publishes **two** webhook systems and Circle Mint uses the older one:

| Version | Delivery | Products |
|---|---|---|
| v2 | Direct HTTPS POST from Circle, `ECDSA_SHA_256`, `X-Circle-Signature` + `X-Circle-Key-Id` headers | Circle Wallets, Circle Contracts, CPN payments, Gateway, StableFX |
| **v1** | **Amazon SNS publishes to your endpoint** | **Circle Mint**, Digital Asset Accounts, CPN Managed Payments |

Confirmed against `https://developers.circle.com/api-reference/webhooks` (fetched live
2026-07-17): "v2: Direct HTTPS POST from Circle | Circle Wallets, Circle Contracts, CPN payments,
Gateway, StableFX" vs. "v1: Amazon SNS publishes to your endpoint | Circle Mint, Digital Asset
Accounts, CPN Managed Payments."

`https://developers.circle.com/api-reference/verify-webhook-signatures` (fetched live 2026-07-17)
confirms that page describes **only** the v2 ECDSA scheme and explicitly disclaims v1: "v1
notifications (Circle Mint, Digital Asset Accounts, CPN Managed Payments) use a different
signature scheme" — directing integrators to contact a Circle rep for v1 verification details.
**Do not implement the `X-Circle-Signature`/ECDSA flow for Circle Mint.** This matches the local
mirror at `docs/circle-mint-docs/howtos/verify-webhook-signatures.md` and
`docs/circle-mint-docs/reference/webhooks-overview.md` (both last verified live 2026-07-07,
re-confirmed live again this session, 2026-07-17 — no drift found).

Since Circle's own docs decline to publish v1 verification specifics, this pipeline verifies
Circle Mint deliveries against the **standard, publicly documented, product-agnostic AWS SNS
message-signing algorithm** — described generically below, not sourced from Circle's docs (there
is nothing Circle-specific about it; it's the same scheme any SNS HTTP(S) subscriber verifies
against).

### 3.2 Envelope fields (outer SNS wrapper)

| Field | Type | Notes |
|---|---|---|
| `Type` | string | `Notification` for events, `SubscriptionConfirmation` for the one-time handshake, `UnsubscribeConfirmation` |
| `MessageId` | string | Delivery id; reused on SNS redelivery — the dedup key (§2.3) |
| `TopicArn` | string | The SNS topic bound to the subscription |
| `Message` | string | Circle's JSON envelope, as a JSON-encoded string (§2.5) |
| `Signature` | string | Base64-encoded signature of the canonical message string |
| `SigningCertURL` | string | URL of the X.509 cert used to verify `Signature` |

### 3.3 Subscription handshake

Confirmed live 2026-07-17 (`https://developers.circle.com/api-reference/webhook-endpoints`):

1. Register once via `POST /v1/notifications/subscriptions` with body `{ endpoint }`. "The same
   request shape works for Circle Mint, Digital Asset Accounts, and CPN Managed Payments." No
   `notificationTypes` filter exists on this call — "v1 subscriptions deliver all account events;
   filtering by `notificationTypes` isn't supported" (confirmed live) — topic filtering is
   entirely the application-side dispatch in §2.2/§2.4.
2. SNS then POSTs a one-time `SubscriptionConfirmation` message to the registered endpoint,
   carrying a `SubscribeURL`. The handshake completes by issuing a **server-side `GET`** to that
   URL (not a human clicking a link) — "Open it in your browser, or have your endpoint fetch it
   server-side, to finish the handshake. The subscription status moves to `confirmed` and events
   begin flowing."
3. **Idempotent registration is required**: check `GET /v1/notifications/subscriptions` for an
   existing confirmed subscription to this endpoint before calling `POST` again, because of the
   subscription cap below — an un-guarded redeploy that re-registers would collide with it.

**Subscription cap — confirmed live 2026-07-17**: the v1 notifications section states an "Active
subscription cap" of **Sandbox: 3, Production: 1**, with the table reading "Sandbox | 3 | After 30
days" and "Production | 1 | After 72 hours" (the second column is the cap, the third an
expiry/cleanup window for unconfirmed subscriptions). Phase_3 Task 5's claim of this exact cap is
confirmed correct.

### 3.4 Signature verification algorithm (standard AWS SNS message signing)

Not Circle-specific — this is the generic, publicly documented algorithm every SNS HTTP(S)
subscriber implements, cited here without a fresh AWS-doc fetch per this session's ground rules
(Phase_3 Task 6 description checked for internal consistency below, no discrepancy found):

1. **Build the canonical string** from the SNS message's own fields, in AWS's fixed field order.
   The field set and order **differ by message `Type`**:
   - `Notification`: `Message`, `MessageId`, `Subject` (only if present), `Timestamp`, `TopicArn`,
     `Type` — each as `"<fieldName>\n<fieldValue>\n"` concatenated in that order.
   - `SubscriptionConfirmation` / `UnsubscribeConfirmation`: `Message`, `MessageId`, `SubscribeURL`,
     `Timestamp`, `Token`, `TopicArn`, `Type` — a different field set (no `Subject`, adds
     `SubscribeURL`/`Token`). Both message types must be supported by the verifier since the
     handshake message (§3.3 step 2) and steady-state `Notification` deliveries arrive on the same
     endpoint and need different canonicalization.
2. **Fetch the cert** from `SigningCertURL`, but **validate the URL's host first** — it must match
   the AWS SNS certificate domain pattern (`sns.<region>.amazonaws.com`) before it is fetched.
   This check is what makes the verification real: an implementation that fetches and trusts
   whatever host `SigningCertURL` names, without this pattern check, lets a forged
   `SigningCertURL` defeat verification entirely (an attacker-controlled cert would "verify" an
   attacker-signed forged payload).
3. **Verify** the base64-decoded `Signature` against the canonical string using the cert's public
   key. Algorithm depends on `SignatureVersion`: `SHA1withRSA` for `"1"`, `SHA256withRSA` for
   `"2"` — both must be supported since AWS allows either per-topic.
4. **Cache** the fetched cert keyed by `SigningCertURL` to avoid a network round-trip per webhook
   delivery — the cert is static for a given URL, so this is a pure latency/reliability win, not
   a correctness requirement.

Phase_3 Task 6's description is internally consistent with the above (field-order-differs-by-type,
domain validation before fetch, dual signature-version support, cert caching) — no discrepancy
found against this generic algorithm.

### 3.5 Rejection path

Any delivery that fails signature verification (bad signature, cert domain mismatch, cert fetch
failure, unparseable envelope) is **rejected before it reaches the inbox** — no
`WebhookInboxEntry` row is written, since PRD §10 item 2 requires unverifiable messages to be
"rejected and logged," not stored. The controller returns HTTP 403 and logs the rejection
(including `MessageId` for correlation, since ordinary log aggregation is the only trace of a
rejected-but-real delivery). This is a different path from "processor threw" (§2.6) — verification
failure never becomes a retryable inbox row.

Webhook endpoint authentication is signature verification alone; the endpoint does **not**
require a `ClientCompanyId` header and is exempt from the tenant-scoping middleware that gates
every other controller (PRD §10 item 7) — the provider is not a registered caller.

---

## 4. Mock-mode equivalent

Mock mode substitutes a same-process emitter for the SNS transport, but produces
**byte-shape-identical** payloads on the real topics (ADR 0007) — the durable inbox → dedup →
per-topic-processor pipeline (§2) is exercised unchanged; only §3 (SNS envelope + signature
verification) is swapped for a mock verifier that always accepts. See `02-mock-mode.md` for the
mock gateway/emitter design, the `MockModeGuard` production-safety check, and per-topic mock
payload generation — not restated here.

---

## 5. Tests required

Per the tier table (`CLAUDE.md`):

- **Application (xUnit v3, Moq)** — `WebhookProcessor` against mocked `IWebhookInboxRepository`
  and fake `IWebhookTopicProcessor` implementations:
  - dedup: `TryAddAsync` returns `false` → `Processed`, topic processor never invoked.
  - happy path: `TryAddAsync` returns `true`, processor succeeds → `MarkProcessedAsync` called,
    `Processed` returned.
  - no processor registered for topic → ack-not-retry behavior once the §2.4 correction lands
    (currently: `MarkFailedAsync` called, `Failed` returned — track against the open item in §7).
  - processor throws → `MarkFailedAsync` called with the exception message, `Failed` returned.
- **Infrastructure/Unit** — SNS signature verifier: hand-built canonical message + signature
  against a **self-signed test certificate**, no network access. Cover both `Notification` and
  `SubscriptionConfirmation` canonical-string field orders (§3.4 item 1), both `SignatureVersion`
  values, and the cert-domain-validation rejection path (forged `SigningCertURL` host → rejected
  without a fetch attempt).
- **Api/Integration (WebApplicationFactory + Testcontainers)** — full pipeline round trip:
  - redelivered `MessageId` is not reprocessed (dedup at the HTTP boundary) — mirrors Phase_1's
    `WebhookDedupTests`.
  - unverifiable signature → 403, no inbox row written.
  - `SubscriptionConfirmation` message → handshake completes (fetch-`SubscribeURL` call observed
    via test double), 200, no inbox row written (it's transport handshake, not a business event).
  - unknown/unhandled topic → stored + acknowledged (200), not 500 — this test currently would
    fail against the literal Phase_1 code per §2.4's flagged correction; it should be written to
    assert the *intended* PRD §10 behavior, which doubles as the regression test proving the fix.
  - replay (once implemented, §2.6) — re-running a stored `Failed` entry against a since-fixed or
    now-idempotent processor transitions it to `Processed`.

---

## 6. Interfaces at a glance

```
Application.Webhooks
├── WebhookInboxEntry            (durable row)
├── WebhookProcessingStatus      (Processed | Failed — see §2.4/§7 re: Unhandled)
├── IncomingWebhookEvent         (Topic, ProviderEventId, PayloadJson)
├── IWebhookTopicProcessor       (Topic; ProcessAsync(payloadJson, ct))
├── WebhookProcessor             (the dispatcher — HandleAsync)
└── Ports/
    └── IWebhookInboxRepository  (TryAddAsync / MarkProcessedAsync / MarkFailedAsync)

Infrastructure
├── Persistence/WebhookInboxRepository   (EF-backed IWebhookInboxRepository)
├── Webhooks/<SnsSignatureVerifier>      (Phase 1: mock verifier; Phase 3: real AWS SNS verifier)
└── Providers/Circle/CircleWebhookSubscriptionService  (POST /v1/notifications/subscriptions + handshake)

Api
└── Webhooks/CircleWebhooksController    (receives SNS POST, verifies, unwraps, calls WebhookProcessor)
```

---

## 7. Open corrections / decisions log

Each Circle-specific claim below is cited against a live fetch this session (2026-07-17) or an
already-live-verified local mirror re-confirmed this session; generic AWS SNS algorithm claims
are marked as such and not re-fetched, per this file's scope.

| # | Claim | Status | Source |
|---|---|---|---|
| 1 | Circle Mint webhooks are v1/SNS-based (`POST /v1/notifications/subscriptions`, SNS handshake, `Signature`+`SigningCertURL`), distinct from v2 ECDSA `X-Circle-Signature` used by Wallets/Contracts/CPN/Gateway/StableFX | **Confirmed**, verified against live Circle docs 2026-07-17 | `https://developers.circle.com/api-reference/webhooks` |
| 2 | v2 signature-verification page explicitly does not cover v1; v1 details require contacting a Circle rep | **Confirmed**, verified against live Circle docs 2026-07-17 | `https://developers.circle.com/api-reference/verify-webhook-signatures` |
| 3 | v1 subscription registration is `POST /v1/notifications/subscriptions` with `{ endpoint }`, no topic filter, `SubscriptionConfirmation`/`SubscribeURL` handshake completed by a GET | **Confirmed**, verified against live Circle docs 2026-07-17 | `https://developers.circle.com/api-reference/webhook-endpoints` |
| 4 | Subscription cap: Production = 1, Sandbox = 3 | **Confirmed**, verified against live Circle docs 2026-07-17 | `https://developers.circle.com/api-reference/webhook-endpoints` |
| 5 | `idempotencyKey` is a Circle request-**body** field, not an HTTP header | **Confirmed** — this note is not itself a webhook-pipeline fact (no idempotency key flows through inbound webhooks), included because Phase_3's Global Constraints asserted it as a cross-cutting fact; checked directly against local ground truth | `docs/circle-mint-docs/Core Functionality.postman_collection.json` (`idempotencyKey` appears only as a request-body field across every mutating endpoint sampled) |
| 6 | AWS SNS canonical-string field order differs for `Notification` vs `SubscriptionConfirmation`/`UnsubscribeConfirmation`; `SignatureVersion` `1`→SHA1withRSA, `2`→SHA256withRSA; cert domain must match `sns.<region>.amazonaws.com` | Standard, publicly documented AWS SNS message-signing algorithm — not Circle-specific, not re-fetched this session per scope; Phase_3 Task 6's description checked for internal consistency against this and found consistent (no discrepancy) | Generic AWS SNS docs (not re-fetched) |
| 7 | **Open — not yet resolved by any source doc**: `WebhookProcessor`'s "no processor registered for topic" branch currently returns `Failed` (→ HTTP 500 → SNS redelivers forever), contradicting PRD §10 item 1's "unhandled topics stored + acknowledged, not errors." | **Defect flagged, unresolved** — needs a third status (`Unhandled`) or equivalent, mapped to HTTP 200 in the controller, distinct from the retryable `Failed` path | This file, §2.4 |
| 8 | **Open — not yet specified**: dead-letter threshold (how many `Attempts`/how long before an entry is considered dead-lettered) and the alerting mechanism (PRD §10 item 5) | **Unresolved** — no concrete number or alerting hook in PRD/Phase_1/Phase_3 | PRD §10 item 5 states the requirement but not a threshold; not found elsewhere |
| 9 | **Open — not yet specified**: the admin replay endpoint/port (PRD §10 item 6) has no file/interface listed in Phase_1 or Phase_3 task lists | **Unresolved** — replay is a stated requirement with no assigned task; likely belongs in the Admin module per module boundaries (ADR "B0.5") | PRD §10 item 6; absent from Phase_1 Task 5 and Phase_3 Tasks 5–6 |

No discrepancy was found between PRD §10, Phase_1 Task 5, and Phase_3 Tasks 5–6 on the core
mechanics (inbox, dedup, per-topic dispatch, v1/SNS transport, subscription cap) — the three docs
agree. The two defects found (#7 unhandled-topic status, and the general dead-letter/replay gaps
#8–#9) are gaps in the *plan*, not contradictions between docs, and are recorded here rather than
silently "fixed" in this doc, since fixing them is a code change this file doesn't make.
