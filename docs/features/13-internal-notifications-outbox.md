# 13 — Internal Notifications Outbox (outbound callbacks)

Owns the **outbound** side of event notification: telling this product's own internal consumer
service (legacy applications, the portal) that a state change happened, so those consumers don't
have to poll. Module: `Application.Webhooks` per ADR 0001 (`docs/adr/0001-module-boundaries.md`),
whose module table assigns "Durable inbox, dedup, per-topic processors, **notification outbox**"
to `Webhooks` despite this being an outbound, not inbound, mechanism.

**Not owned here** — the *inbound* Circle webhook pipeline (durable inbox, dedup, per-topic
dispatch, SNS transport) is a separate, unrelated mechanism; see `03-webhook-processing.md`. Both
happen to live in the same module and both use a durable-row-before-side-effect pattern, but one
is "Circle tells us" and this one is "we tell our own consumer" — do not conflate the two
"inbox vs outbox" names. This file borrows the *shape* of that durability pattern (write the
durable row first, let a separate process do the unreliable I/O) but the two tables, ports, and
dispatch loops are independent.

---

## 1. Scope / PRD requirement

PRD §10.1 (Internal event notifications, decided 2026-07-12: HTTP callback / all state changes /
outbox + retry / Phase 1):

> When a state change lands in this service — entity registration accepted or rejected, deposit
> credited, recipient approved, transfer completed or failed, redemption completed or failed, and
> every other externally meaningful status transition — the service notifies an internal consumer
> service by POSTing a JSON event to a configured endpoint, so legacy applications and the portal
> do not have to poll.

Requirements as stated:

1. **Mechanism**: HTTP POST of a JSON event to an internal-service endpoint; endpoint URL and auth
   credentials come from configuration.
2. **Coverage**: all state changes, provider-webhook-driven and API-initiated alike.
3. **Envelope**: a consistent contract across all event types — event type, `ClientCompanyId`,
   entity/transaction id, occurred-at timestamp, correlation id, and an event-specific payload.
4. **Delivery guarantee — outbox pattern**: the `NotificationOutboxEntry` row is written in the
   same database transaction as the state change it announces (composes with the reserve →
   gateway/state-transition → complete two-`SaveChangesAsync` idempotency pattern — see
   `05-reliability-and-error-handling.md` §2.2, not redefined here), then a background dispatcher
   POSTs it with bounded retries and backoff. A notification is never lost because the process
   crashed between the state change and the send; failed deliveries stay queued.
5. **Ordering/duplication contract**: delivery is at-least-once and may reorder under retry;
   consumers deduplicate by event id and must tolerate replays.
6. **Phasing**: shipped in Phase 1, exercised in the demo via a stub receiver endpoint standing in
   for the real internal service (§15.1). Dead-letter handling, replay, and delivery observability
   follow in Phase 2 (§15.2) — explicitly **out of scope** for this file's implementation, not
   silently omitted.

### 1.1 Scope discipline — only five call sites, not "every mutating handler"

PRD §10.1 requirement 2 literally says "all state changes." Phase_1_Feature_Slices.md Task 13
narrows this deliberately: the PRD §15.1 demo script only exercises five transitions, and every
*other* mutating handler (create sub-account, register recipient, create transfer/redemption,
create linked bank account) is an **API-initiated** action where the caller already gets a
synchronous HTTP response carrying the outcome — a polling consumer has no gap to fill there. The
five wired this ticket, all **provider-webhook-driven** transitions where the caller has no
synchronous signal:

| # | Transition | Handler |
|---|---|---|
| 1 | Entity registration decision (`Accepted`/`Rejected`) | `ProcessExternalEntityDecisionHandler` |
| 2 | Deposit credited | `ProcessDepositCommandHandler` (`RecordCompleteAsync` only) |
| 3 | Recipient approval decision | `ProcessRecipientDecisionHandler` |
| 4 | Transfer completion | `ProcessTransferStatusCommandHandler` |
| 5 | Redemption completion | `ProcessPayoutStatusCommandHandler` |

Resist the urge to wire more — this is a scope boundary carried forward from the ticket
(`.scratch/treasury-service-orchestrator/issues/09-notifications-outbox.md`), not an oversight.
PRD §15.1 slice 11's acceptance line names exactly these five: "each state change (entity
decision, deposit credit, recipient approval, transfer completion, redemption completion) also
arrives as an internal-notification callback at the stub receiver."

### 1.2 §15.1 slice 11 — the demo-script requirement this feature proves

PRD §15.1 lists this as slice 11 of the Phase 1 demo script: "Internal event notifications:
`NotificationOutboxEntry` written transactionally with each state change, background HTTP
dispatcher with retry, stub receiver endpoint (§10.1)" — proving "Consumers learn of state changes
without polling." The demo script's acceptance line (quoted above) is the literal Definition of
Done for this feature: after the five transitions run during the scripted demo, the stub receiver
must have observed all five as HTTP POSTs.

---

## 2. Domain design

### 2.1 `NotificationDeliveryStatus`

```csharp
// src/TreasuryServiceOrchestrator.Domain/NotificationDeliveryStatus.cs
namespace TreasuryServiceOrchestrator.Domain;

public enum NotificationDeliveryStatus
{
    Pending,
    Delivered,
}
```

Only two values in Phase 1 — there is deliberately no `Failed`/`DeadLettered` status yet. Per PRD
§10.1 requirement 4 ("failed deliveries stay queued"), a row that keeps failing simply keeps its
`NextAttemptAtUtc` pushed further out on each unsuccessful attempt and stays `Pending` forever.
Phase 2 (§15.2, PRD's "Notification DLQ + delivery observability" backlog item) adds a
dead-letter state, admin replay, and delivery-lag/failure metrics — not built here.

### 2.2 `NotificationOutboxEntry`

```csharp
// src/TreasuryServiceOrchestrator.Domain/NotificationOutboxEntry.cs
namespace TreasuryServiceOrchestrator.Domain;

public class NotificationOutboxEntry
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public required string ClientCompanyId { get; set; }
    public required string EntityId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string CorrelationId { get; set; }
    public required string PayloadJson { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}
```

This is the PRD §10.1 requirement-3 envelope made concrete: `EventType`, `ClientCompanyId`,
`EntityId` (the entity/transaction id), `OccurredAtUtc`, `CorrelationId`, and `PayloadJson`
(event-specific payload, serialized). `Status`/`AttemptCount`/`NextAttemptAtUtc`/`DeliveredAtUtc`
are dispatcher bookkeeping, not part of the wire envelope (see §5 for the actual POST body shape).

Like `WebhookInboxEntry` (`03-webhook-processing.md` §2.1), this type lives in `Domain` per
Phase_1 Task 13's file list (`src/TreasuryServiceOrchestrator.Domain/NotificationOutboxEntry.cs`)
— note this differs from `WebhookInboxEntry`, which lives in `Application.Webhooks` as a plain
POCO. `NotificationOutboxEntry` is a `Domain` type instead; both placements satisfy the same
constraint (EF Core maps POCOs from any referenced assembly, so a `DbContext` in `Infrastructure`
can reference either without inverting the dependency rule) — the difference is which existing
task's file list each type followed, not a new modeling distinction. `OccurredAtUtc` /
`NextAttemptAtUtc` / `DeliveredAtUtc` are populated via `TimeProvider.GetUtcNow().UtcDateTime` at
every write site (invariant 2), never `DateTime.UtcNow` directly, notwithstanding the plan
excerpt's illustrative use of `DateTime.UtcNow` in test fixture data below.

No domain invariants/state-machine methods on `NotificationOutboxEntry` beyond the two-state enum
— unlike `SubAccount`'s lifecycle state machine (`01-tenancy-and-authorization.md`), there is no
illegal-transition concern here: `Pending → Delivered` is the only transition, and it is set by
the dispatcher (§4.3), not invoked by domain logic.

---

## 3. Application design — ports

Two ports in `Application/Webhooks/Ports/`, alongside (but independent from) the inbound
`IWebhookInboxRepository` (`03-webhook-processing.md` §2.2):

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationOutboxRepository.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface INotificationOutboxRepository
{
    Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken);
}
```

```csharp
// src/TreasuryServiceOrchestrator.Application/Webhooks/Ports/INotificationSender.cs
namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public interface INotificationSender
{
    Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken);
}
```

`AddAsync` deliberately does **not** call `SaveChangesAsync` — it stages the entry via
`DbContext.AddAsync` (through the EF-backed `Infrastructure` implementation) exactly like every
other repository `AddAsync` in this codebase, so the caller's own
`unitOfWork.SaveChangesAsync(cancellationToken)` is what commits it. This is what makes §4.1 (the
same-transaction guarantee) hold without any new transaction-management code: the outbox write
rides inside whichever `SaveChangesAsync` the calling handler was already going to make as the
second half of its reserve → gateway/state-transition → complete pattern.

`INotificationSender.SendAsync` returns `bool`, not throwing on delivery failure by design — a
failed POST is an ordinary, expected outcome for the dispatcher (§4.3) to schedule backoff on, not
an exceptional condition. `HttpNotificationSender` (§4.1) is the only implementation in Phase 1;
its `try/catch (HttpRequestException) => return false` is where that translation happens.

---

## 4. Infrastructure design

### 4.1 `HttpNotificationSender`

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/HttpNotificationSender.cs
public sealed class HttpNotificationSender(HttpClient httpClient, IOptions<NotificationDispatcherOptions> options)
    : INotificationSender
{
    public async Task<bool> SendAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken)
    {
        // POST envelope { eventId, eventType, clientCompanyId, entityId, occurredAtUtc, correlationId, payload } to settings.EndpointUrl
        // optional configured auth header attached via TryAddWithoutValidation
        // try/catch (HttpRequestException) => false; otherwise return response.IsSuccessStatusCode
    }
}
```

Registered via `AddHttpClient<INotificationSender, HttpNotificationSender>()` (invariant 3 —
`IHttpClientFactory`-managed, never `new HttpClient()`), scoped to match the five handlers'
lifetime. The wire envelope this sends is: `eventId` (the outbox row's own `Id`, **not** a
Circle-originated id — this is a locally minted event id, since this is a purely internal
notification with no Circle counterpart), `eventType`, `clientCompanyId`, `entityId`,
`occurredAtUtc`, `correlationId`, `payload` (the deserialized `PayloadJson`).

### 4.2 `NotificationDispatcherOptions`

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcherOptions.cs
public sealed class NotificationDispatcherOptions
{
    public string EndpointUrl { get; set; } = "http://localhost:5080/internal/notifications";
    public string? AuthHeaderName { get; set; }
    public string? AuthHeaderValue { get; set; }
    public int MaxBatchSize { get; set; } = 20;
    public int PollingIntervalMilliseconds { get; set; } = 500;
    public int BaseBackoffMilliseconds { get; set; } = 1000;
    public int MaxBackoffMilliseconds { get; set; } = 60000;
}
```

Bound from the `Notifications` config section. `AuthHeaderName`/`AuthHeaderValue` satisfy PRD
§10.1 requirement 1's "auth credentials come from configuration" — Phase 1's stub receiver ignores
them; the real internal service's own auth scheme is Phase 2.

### 4.3 `NotificationDispatcher` — bounded backoff

```csharp
// src/TreasuryServiceOrchestrator.Infrastructure/Notifications/NotificationDispatcher.cs
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory, IOptions<NotificationDispatcherOptions> options, TimeProvider timeProvider)
{
    public async Task<int> DispatchDueBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var settings = options.Value;

        var due = await outbox.GetDueBatchAsync(settings.MaxBatchSize, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        foreach (var entry in due)
        {
            var delivered = await sender.SendAsync(entry, cancellationToken);
            if (delivered)
            {
                entry.Status = NotificationDeliveryStatus.Delivered;
                entry.DeliveredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }
            else
            {
                entry.AttemptCount++;
                var backoffMilliseconds = Math.Min(
                    settings.BaseBackoffMilliseconds * (1 << Math.Min(entry.AttemptCount, 10)),
                    settings.MaxBackoffMilliseconds);
                entry.NextAttemptAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(backoffMilliseconds);
            }
        }

        if (due.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}
```

Backoff is capped exponential: `BaseBackoffMilliseconds * 2^min(AttemptCount, 10)`, clamped to
`MaxBackoffMilliseconds` — the `Math.Min(entry.AttemptCount, 10)` shift-count clamp exists so the
bit-shift itself never overflows on a row that fails for a very long time (Phase 2's dead-letter
threshold is exactly the mechanism that would otherwise stop this growth; Phase 1 has no such
stop, per requirement 4's "failed deliveries stay queued").

`DispatchDueBatchAsync` resolves its three dependencies through a fresh `IServiceScopeFactory`
scope per call rather than taking them as constructor dependencies directly, because
`NotificationDispatcher` itself is registered singleton (it's driven by a single
`BackgroundService`, §4.4) while `INotificationOutboxRepository`/`IUnitOfWork`/`DbContext` are
scoped — the per-call scope is what makes a singleton-lifetime dispatcher safe to use against
scoped EF Core state.

### 4.4 `NotificationDispatchBackgroundService`

A `BackgroundService` polling `DispatchDueBatchAsync` on `PollingIntervalMilliseconds`, swallowing
`OperationCanceledException` on host shutdown. Registered via
`AddHostedService<NotificationDispatchBackgroundService>()`. This is the one piece of the feature
that is not request-driven — it runs continuously for the process lifetime, same operational shape
as the reconciliation background service in `05-reliability-and-error-handling.md` §7.5.

### 4.5 `NotificationOutboxRepository` — EF mapping

```csharp
public sealed class NotificationOutboxRepository(TreasuryServiceOrchestratorDbContext dbContext) : INotificationOutboxRepository
{
    public async Task AddAsync(NotificationOutboxEntry entry, CancellationToken cancellationToken) =>
        await dbContext.NotificationOutboxEntries.AddAsync(entry, cancellationToken);

    public async Task<IReadOnlyList<NotificationOutboxEntry>> GetDueBatchAsync(
        int batchSize, DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.NotificationOutboxEntries
            .Where(e => e.Status == NotificationDeliveryStatus.Pending && (e.NextAttemptAtUtc == null || e.NextAttemptAtUtc <= nowUtc))
            .OrderBy(e => e.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
}
```

`DbContext` additions: `DbSet<NotificationOutboxEntry> NotificationOutboxEntries`, plus in
`OnModelCreating` a composite index on `(Status, NextAttemptAtUtc)` — the exact predicate
`GetDueBatchAsync` filters on, so the dispatcher's poll doesn't scan the whole table every 500ms —
and `ClientCompanyId` mapped `nvarchar(450)` with the same collation used everywhere else
`ClientCompanyId` is a column (see `01-tenancy-and-authorization.md` for why the explicit
collation matters for cross-tenant-safe comparisons).

---

## 5. The five wired call sites

All five follow the identical shape: add `INotificationOutboxRepository outbox` as a constructor
dependency, and insert an `await outbox.AddAsync(new NotificationOutboxEntry { ... }, ct);` call
**before** the handler's existing (unmodified) `unitOfWork.SaveChangesAsync(cancellationToken)`.
No handler gets a new `SaveChangesAsync` call — CLAUDE.md invariant 11's two-phase pattern already
supplies the transaction boundary; the outbox write just joins the existing second phase's
pending change set.

| Handler | Trigger condition | `EventType` | `EntityId` | `CorrelationId` |
|---|---|---|---|---|
| `ProcessExternalEntityDecisionHandler` | `newStatus is Accepted or Rejected` | `EntityRegistrationDecided` | `subAccount.Id` | `command.WalletId` |
| `ProcessDepositCommandHandler` | `RecordCompleteAsync` only (not `RecordFailedAsync`'s currency-mismatch path) | `DepositCredited` | `transaction.Id` | `command.CorrelationId` |
| `ProcessRecipientDecisionHandler` | every decision | `RecipientApprovalDecided` | `recipient.Id` | `command.CircleRecipientId` |
| `ProcessTransferStatusCommandHandler` | `newStatus == TransferStatus.Complete` | `TransferCompleted` | `transfer.Id` | `command.CircleTransferId` |
| `ProcessPayoutStatusCommandHandler` | `newStatus == TransferStatus.Complete` | `RedemptionCompleted` | `redeemRequest.Id` | `command.CircleRedeemId` |

`PayloadJson` is `JsonSerializer.Serialize(new { ... })` of a handler-local anonymous object
carrying only the fields that event type's consumer needs (e.g. redemption's payload carries
`GrossAmount`/`Fees`/`NetAmount` — the same three fields the ledger's own redemption record
exposes, per `04-ledger-and-balances.md`). There is no shared payload DTO type across the five
event types — each handler owns its own anonymous shape, matching the "event-specific payload"
half of the PRD §10.1 requirement-3 envelope; the fixed fields (`EventType`, `ClientCompanyId`,
`EntityId`, `OccurredAtUtc`, `CorrelationId`) are the consistent half.

`OccurredAtUtc` in every site is read from the entity's own `UpdatedAtUtc` (already set via
`TimeProvider` earlier in the same handler for the state transition itself), not a fresh
`TimeProvider` call at the outbox-write site — the notification's timestamp is the state change's
timestamp, not the (near-identical but technically later) moment the outbox row was staged.

---

## 6. Stub receiver — `InternalNotificationsStubController`

```csharp
// src/TreasuryServiceOrchestrator.Api/Webhooks/InternalNotificationsStubController.cs
[ApiController]
[Route("internal/notifications")]
public sealed class InternalNotificationsStubController(ILogger<InternalNotificationsStubController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Receive([FromBody] JsonElement payload)
    {
        logger.LogInformation("Stub internal-notification receiver got event: {Payload}", payload.GetRawText());
        return Ok();
    }
}
```

Lives at `Api/Webhooks/` (module-scoped path, matching the existing `Api/Compliance/…` convention
— not a flat `Api/Controllers/`) despite being an outbound-notification receiver, because it is
still `Webhooks`-module territory per ADR 0001. Deliberately carries **no** `[ApiVersion]`/
`api/v{version:apiVersion}` route prefix, unlike every other controller in this API — it stands in
for an external internal service's endpoint, not a versioned resource this API's own clients
consume, so URI versioning (PRD §14 API standards) does not apply to it.

### 6.1 `CallerIdentityMiddleware` bypass

The stub receiver is not a registered `ClientCompanyId` caller — the dispatcher POSTs to it with
no `ClientCompanyId` header at all — so it needs a bypass in
`src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs`
(`CallerIdentityMiddleware`, not `ClientCompanyIdMiddleware` — confirmed against the actual class
name in the shipped file). As of this write-up the shipped middleware
(`git log` shows it introduced under Task 1 and unmodified since) has **no bypass mechanism at
all** — it unconditionally requires the `ClientCompanyId` header:

```csharp
public sealed class CallerIdentityMiddleware(RequestDelegate next)
{
    private const string HeaderName = "ClientCompanyId";

    public async Task InvokeAsync(
        HttpContext context, HttpCallerContext callerContext,
        IOptions<CallerIdentityOptions> options, ISubAccountRepository subAccountRepository)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var callerId) || string.IsNullOrWhiteSpace(callerId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        // ...
    }
}
```

This feature adds the first bypass entry — a plain `static readonly string[] BypassPaths` field
(no registry type exists in this middleware yet), checked first in `InvokeAsync`:

```csharp
private static readonly string[] BypassPaths = ["/internal/notifications"];

public async Task InvokeAsync(HttpContext context, /* ... */)
{
    if (BypassPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
    {
        await next(context);
        return;
    }
    // existing header-check logic, unchanged
}
```

The stub is authenticated by a different mechanism entirely (nothing, in Phase 1 — the real
internal service's shared-secret auth is Phase 2, per §4.2's `AuthHeaderName`/`AuthHeaderValue`
existing in config already but unused for inbound verification since the stub doesn't check them).
Later features needing a bypass (health checks, the Circle webhook endpoint of
`03-webhook-processing.md` §3.5) extend this same array rather than inventing a second mechanism.

---

## 7. Tests required

Per the tier table (`CLAUDE.md`) and the ticket's Definition of Done
(`.scratch/treasury-service-orchestrator/issues/09-notifications-outbox.md`):

- **Domain (xUnit v3)** — `NotificationOutboxEntry` state transitions: default `Status` is
  `Pending`; setting `Status = Delivered` alongside `DeliveredAtUtc` is a plain property test since
  there's no domain method encapsulating the transition (§2.2) — this test exists mainly to pin
  the shape, not to exercise invariant logic.
- **Infrastructure/Unit (xUnit v3, NSubstitute)** — `NotificationDispatcherTests`, three cases
  matching Phase_1 Task 13 Step 3 exactly:
  - sender succeeds → entry transitions to `Delivered`, `DeliveredAtUtc` set,
    `unitOfWork.SaveChangesAsync` called once.
  - sender fails → entry stays `Pending`, `AttemptCount` incremented, `NextAttemptAtUtc` pushed
    into the future (bounded backoff exercised).
  - no due entries → `DispatchDueBatchAsync` returns 0 and `SaveChangesAsync` is **not** called —
    this is the guard against an empty-batch dispatcher tick writing a no-op transaction every
    poll interval.
- **Api/Integration (WebApplicationFactory + Testcontainers, real SQL Server)** —
  `NotificationOutboxDeliveryTests`:
  - **Same-transaction guarantee**: the ticket's Definition of Done calls for "state-change +
    outbox-write atomicity (kill mid-transaction scenario or equivalent — same-transaction
    guarantee must be exercised, not just asserted)." The Phase_1 Task 13 excerpt's own
    integration test (`Deposit_credit_produces_a_delivered_internal_notification`) exercises the
    *end-to-end* outcome (deposit completes → outbox row appears → dispatcher delivers it to the
    real stub controller inside the same test host) but does **not** literally kill the process
    mid-transaction — it is a positive-path proof, not a fault-injection proof. A true
    same-transaction guarantee test needs a second case this ticket's plan does not yet specify:
    inject a failure between the handler's `outbox.AddAsync` call and its `SaveChangesAsync`
    (e.g. a `DbContext` interceptor or a poisoned `SaveChangesAsync` override in a test-only
    derived context that throws after staging both changes) and assert **neither** the state
    change nor the outbox row persisted — proving the two writes share one transaction rather than
    merely usually landing together. This is flagged as an open gap, §8 item 1 — the happy-path
    test alone does not prove atomicity, only that it works when nothing fails.
  - all five wired transitions reach `InternalNotificationsStubController` — the plan's excerpt
    shows only the deposit-credit case in full; the other four (entity decision, recipient
    decision, transfer completion, redemption completion) need the same shape (drive the state
    change through its normal API/mock-webhook path, poll the outbox table for a `Delivered` row
    of the matching `EventType`) — DoD explicitly requires "`InternalNotificationsStubController`
    receives all five wired transitions in an integration test," which is broader than the single
    example transcribed in the plan.
  - redelivery/dedup is **not** a concern on this side — unlike the inbound pipeline
    (`03-webhook-processing.md` §2.3), there is no incoming dedup key to check; the guarantee here
    is at-least-once *outbound* delivery (PRD §10.1 requirement 5), so the consumer (the stub, and
    eventually the real internal service) is the one responsible for deduplicating by `eventId` —
    nothing to unit-test on this side beyond confirming the envelope actually carries a stable
    `eventId` (§4.1).

---

## 8. Open corrections / decisions log

| # | Claim | Status | Resolution |
|---|---|---|---|
| 1 | Ticket DoD requires a same-transaction "kill mid-transaction scenario or equivalent" test; the Phase_1 Task 13 plan excerpt's own integration test is a positive-path proof only | **Gap, not a contradiction** — flagged in §7; a fault-injection test (poisoned/throwing `SaveChangesAsync` after both writes are staged, asserting neither persisted) needs to be added when this feature is implemented, since the plan excerpt doesn't specify one | This file, §7 |
| 2 | Ticket originally referenced `ClientCompanyIdMiddleware` as the file needing a bypass | **Corrected this session** — the real class is `CallerIdentityMiddleware` (`src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs`), confirmed by reading the shipped file; the ticket file (`.scratch/treasury-service-orchestrator/issues/09-notifications-outbox.md`) already reflects this correction as of this session — no further action needed | `.scratch/treasury-service-orchestrator/issues/09-notifications-outbox.md`; `src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs` |
| 3 | Shipped `CallerIdentityMiddleware` has no bypass mechanism yet | **Confirmed** by reading the file directly (2026-07-17) — it unconditionally 401s any request missing the `ClientCompanyId` header; this feature adds the first `BypassPaths` entry (§6.1) | `src/TreasuryServiceOrchestrator.Api/Middleware/CallerIdentityMiddleware.cs` |
| 4 | `NotificationOutboxEntry` lives in `Domain`, while the inbound-pipeline's `WebhookInboxEntry` lives in `Application.Webhooks` | **Confirmed, not a defect** — both are plain POCOs EF Core can map from either assembly; the placement difference just follows each type's originating task's file list (Phase_1 Task 5 vs. Task 13) rather than a deliberate modeling rule. No action needed, noted in §2.2 so a future reader doesn't "fix" it into false consistency. | `docs/Phase_1_Feature_Slices.md` Task 5 vs. Task 13 file lists |
| 5 | This feature is purely internal — no Circle API calls, no Circle-fact verification performed for this file, per this file's own scope | **By design** — `eventId`/envelope fields are locally minted, not Circle-sourced; nothing in this file required a live Circle-docs check | N/A |

No discrepancy was found between PRD §10.1/§15.1, the Phase_1 Task 13 plan, and the ticket
(`.scratch/…/09-notifications-outbox.md`) on the core mechanics (outbox shape, five call sites,
same-transaction requirement, stub receiver, Phase 2 deferrals) — the docs agree. The two items
worth carrying forward into implementation are #1 (the fault-injection test gap) and #3 (the
middleware currently has zero bypass infrastructure, so this feature's diff necessarily touches a
shared cross-cutting file — coordinate with whichever other Phase 1 task lands second if two
features need `BypassPaths` concurrently).
