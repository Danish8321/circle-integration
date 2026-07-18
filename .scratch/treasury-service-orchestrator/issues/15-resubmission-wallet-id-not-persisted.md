Status: resolved

Source: found 2026-07-18 while implementing ticket 10 (`10-demo-script-e2e.md`) — driving the
real reject→resubmit→accept path end to end through the mock webhook pipeline surfaced a
structural bug in already-shipped code, not a test-authoring problem. Blocks ticket 10 from going
fully green (the resubmission step of the demo script cannot be asserted through to `Active`).

Blocks: 10-demo-script-e2e.

## Scope

`ResubmitEntityRegistrationHandler.ResubmitAsync`
(`src/TreasuryServiceOrchestrator.Application/Compliance/ResubmitEntityRegistration/ResubmitEntityRegistrationHandler.cs`,
~lines 68-89) calls `gateway.CreateExternalEntityAsync(...)` again on resubmission and gets back a
**new** `gatewayResult.WalletId` — Circle (and the mock, `MockSubAccountGateway.CreateExternalEntityAsync`)
issues a fresh wallet id per `externalEntities` create call, resubmission included. That new
wallet id is written onto the new `EntityRegistration` row only. Unlike `CreateSubAccountHandler`
(which calls `subAccount.BeginCompliance(gatewayResult.WalletId)` right after its own gateway
call), `ResubmitEntityRegistrationHandler` never calls `subAccount.BeginCompliance(...)` (or any
equivalent) to update `subAccount.CircleWalletId` to the new wallet id.

Consequence: the scheduled `externalEntities` decision webhook for a resubmission — real Circle
SNS delivery or the mock's scheduled one — always carries the *new* wallet id.
`ProcessExternalEntityDecisionHandler.HandleAsync` looks the sub-account up via
`ISubAccountRepository.GetByCircleWalletIdAsync(command.CircleWalletId, ...)`, which still holds
the *original* (pre-resubmission) wallet id. The lookup throws `NotFoundException`, the webhook
inbox entry is marked failed (webhook delivery is one-shot per event id — no retry drives this to
success later), and the sub-account is **structurally stuck in `PendingCompliance` forever** after
any resubmission. This is not a mock-only artifact — `CircleWalletId` is the same field a live
Circle SNS decision webhook keys off of, so this reproduces against the real provider too.

Confirmed by driving `DemoScriptEndToEndTests` (ticket 10) through the real pipeline: the initial
create→webhook→`Active` transition works; reject→resubmit→webhook leaves the sub-account in
`PendingCompliance`.

## Definition of done

- `ResubmitEntityRegistrationHandler.ResubmitAsync` updates `subAccount.CircleWalletId` to the
  resubmission's `gatewayResult.WalletId` (mirroring `CreateSubAccountHandler`'s
  `subAccount.BeginCompliance(gatewayResult.WalletId)` call) so the subsequent decision webhook
  can find the sub-account.
- Confirm `SubAccount`'s state machine allows re-setting `CircleWalletId` from
  `PendingCompliance` (via `ResubmitCompliance()`'s resulting state) — `BeginCompliance` may need
  a second call path, or a dedicated method, depending on what invariants `SubAccount` already
  enforces on that transition; read the entity before picking the mechanism. Stale
  entity-registration/webhook-inbox rows keyed to the old wallet id are harmless history, not
  touched by the fix.
- A unit/handler test exercises resubmission end to end through `ProcessExternalEntityDecisionHandler`
  (mirroring `NotificationOutboxDeliveryTests.EntityRegistrationDecided_IsEventuallyDelivered`
  but for the resubmit path) proving the sub-account reaches `Active` after resubmission's webhook.
- `10-demo-script-e2e.md`'s `DemoScriptEndToEndTests` asserts the resubmitted sub-account B reaches
  `SubAccountLifecycleState.Active` after its webhook is dispatched (currently asserts only
  `PendingCompliance` immediately post-resubmit, working around this gap) — update that assertion
  once this ticket ships.
- `check.sh` clean, `test-fast.sh`/`test-full.sh` green including the new assertions.

## Comments

Resolved 2026-07-18: added `SubAccount.UpdateCircleWalletId(string)` and called it in
`ResubmitEntityRegistrationHandler.ResubmitAsync` right after `gatewayResult` comes back.
`DemoScriptEndToEndTests`'s resubmission step now asserts `Active`, not `PendingCompliance`.
`test-full.sh`: 53/53 green.
