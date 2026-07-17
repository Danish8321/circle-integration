Status: resolved

## Solidified 2026-07-17 grilling

Spec `docs/features/13-internal-notifications-outbox.md` is exhaustive. One flagged gap (§8 item
1): the DoD's "kill mid-transaction" atomicity requirement needs a fault-injection test beyond the
plan excerpt's positive-path example — already specified precisely enough to implement (poisoned/
throwing `SaveChangesAsync` after both writes staged, assert neither persisted), not a design
ambiguity. §6.1's `CallerIdentityMiddleware` `BypassPaths` addition also resolves ticket 14 item 2
(see that ticket) — `/internal/notifications` is the first bypass entry.

Source: `docs/features/13-internal-notifications-outbox.md` (old source
`docs/Phase_1_Feature_Slices.md` Task 13, deleted 2026-07-17 — superseded by the per-feature doc
restructure).
Blocked by: 08-admin-cross-tenant-views.

## Scope

Outbox pattern: `NotificationOutboxEntry` row written in the **same DB transaction** as the
state change it announces, then a background HTTP dispatcher POSTs it to a configured internal
endpoint with bounded backoff — survives a crash between state change and send. Stub receiver
controller stands in for the real internal service. Only the five PRD §15.1 demo-script
notification-worthy transitions are wired this ticket: entity decision (Accepted/Rejected),
deposit credit, recipient approval decision, transfer completion, redemption completion. DLQ/
replay/delivery-observability explicitly deferred to Phase 2 (§15.2) — do not build them here.

## Files (see Task 13 for exact list)

- New: `Domain/{NotificationDeliveryStatus,NotificationOutboxEntry}.cs`,
  `Application/Webhooks/Ports/{INotificationOutboxRepository,INotificationSender}.cs`,
  `Infrastructure/Notifications/{NotificationDispatcherOptions,HttpNotificationSender,
  NotificationDispatcher,NotificationDispatchBackgroundService}.cs`,
  `Infrastructure/Persistence/NotificationOutboxRepository.cs`,
  `Api/Webhooks/InternalNotificationsStubController.cs` (module-scoped path, matching the
  existing `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`;
  corrected 2026-07-17; `Webhooks` per ADR 0001's ownership of the notification outbox).
- Modify: `DbContext`, `ProcessExternalEntityDecisionHandler`, `ProcessDepositCommandHandler`,
  `ProcessRecipientDecisionHandler`, `ProcessTransferStatusCommandHandler`,
  `ProcessPayoutStatusCommandHandler`, `CallerIdentityMiddleware`, `Program.cs`.

## Key corrections that apply

- CLAUDE.md invariant 11 (two-`SaveChangesAsync` reserve/complete pattern) applies to the
  outbox write itself — the outbox row must land in the *same* transaction as the state change,
  not a follow-up call.
- Scope discipline from the doc: only the five call sites listed above — resist the urge to wire
  every mutating handler; PRD §10.1 "all state changes" is satisfied by the demo script's actual
  five webhook-driven transitions, per the doc's own reasoning (API-initiated actions already
  get a synchronous response).

## Definition of done

- Domain test: `NotificationOutboxEntry` state transitions.
- Dispatcher tests (Moq for `INotificationSender`) — bounded backoff behavior, delivery status
  transitions.
- Integration test: state-change + outbox-write atomicity (kill mid-transaction scenario or
  equivalent — same-transaction guarantee must be exercised, not just asserted).
- `InternalNotificationsStubController` receives all five wired transitions in an integration
  test.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run (new stub endpoint).

## Comments
