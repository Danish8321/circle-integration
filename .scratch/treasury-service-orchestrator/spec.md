# TreasuryServiceOrchestrator — Phase 1 remaining slices

**Correction (2026-07-17, later same day)**: `docs/Phase_1_Feature_Slices.md` and `docs/PRD.md`
were deleted as part of the per-feature doc restructure — content preserved, split across
`docs/README.md` (index) + `docs/features/01-13-*.md` (one file per module/feature). This spec
and the "Task N" numbering below are a historical snapshot from when the monolithic doc was
current; the ticket-to-task mapping still holds (each `.scratch/.../issues/NN-*.md` ticket's
`Source:` line has been repointed to the surviving `docs/features/*.md` file), but do not go
looking for the old filenames — they're gone. Current source of truth: `docs/README.md`.

Original source of truth: `docs/Phase_1_Feature_Slices.md` (finalized 2026-07-17, doc-grilling +
design-pass corrections applied). This spec covers **remaining** work only — Tasks 1-4 (caller
registry, error/validation pipeline, sub-account entity registration, sub-account endpoints
rework) are already built in `src/` (Compliance module: `CreateSubAccount`, `GetSubAccount`,
`ListSubAccounts`, `SetSubAccountDisabled`, `ResubmitEntityRegistration`), including the
caller-registry security fix (`CallerIdentityMiddleware` + `ISubAccountRepository`, 2026-07-17).
Since this spec was written, Task 5 (webhook pipeline core) has also been substantially built —
see `.scratch/treasury-service-orchestrator/issues/01-webhook-pipeline-core.md` for current
status.

## Goal

Rebuild the feature surface around `docs/PRD.md` — webhook-driven ledger (deposits, transfers,
redemptions), recipients, balances, admin cross-tenant views, and an outbox-based internal
notifications pipeline — so the running system matches all eleven PRD §15.1 Phase 1 slices
end to end, gated by a single Task 14 demo-script E2E test.

## Architecture

Clean/Onion (Domain → Application → Infrastructure → Api). Every mutating handler: two
`SaveChangesAsync` idempotency pattern (reserve → gateway/side-effect → complete). Circle Mint
calls stay behind `IStablecoinGateway`/`ISubAccountGateway`; Phase 1 runs mock mode only (real
Circle HTTP is Phase 3). Webhooks land in a durable inbox (Task 5), deduped, dispatched to
per-topic processors in the same transaction as their side effects.

Module boundaries (ADR 0001, B0.5): `Compliance` | `Ledger` | `Webhooks` | `Admin` | `Shared`
sub-namespaces under `Application`/`Domain` only — `Infrastructure` stays flat
(`Infrastructure/Providers/Circle/`, `Infrastructure/Mocks/`, `Infrastructure/Persistence/`,
`Infrastructure/Notifications/`).

## Sequencing (hard dependency chain)

```
01 webhook-pipeline-core          (Phase 1 Task 5)
   -> 02 mock-provider-gateway    (Task 6)
      -> 03 deposit-address-generation  (Task 7)
         -> 04 ledger-transaction-and-balance  (Task 8)
            -> 05 recipients               (Task 9)
            -> 06 outbound-transfers        (Task 10)
               -> 07 redemption-and-linked-bank-account  (Task 11)
                  -> 08 admin-cross-tenant-views  (Task 12)
                     -> 09 notifications-outbox   (Task 13)
                        -> 10 demo-script-e2e     (Task 14)
```

Ticket numbers (01-10) are this tracker's own numbering, distinct from the Phase 1 Task numbers
cited in each ticket's `Source:` line — don't conflate the two when looking a ticket up.

05, 06, 07 all depend on 04 (shared ledger-posting module, corrections item 2). 08 depends on
04+06+07 (transaction filter rework, master-account summary needs balances). 09 depends on
04/05/06/07 (wires notification calls into their handlers). 10 depends on everything.

## Binding corrections (override any contradicting snippet in the source doc)

All nine "doc-grilling 2026-07-17" items and all nine "design-pass corrections 2026-07-17" items
in `docs/Phase_1_Feature_Slices.md` (lines 43-72) apply to every ticket below without
re-statement. Highest-traffic ones:

- Mocking library is **Moq**, not NSubstitute — translate every test snippet before pasting.
- `TenantScopeResolver` returns `TenantScope` (`Single(string) | AllTenants)`, not `string?`.
- `FundAccount.Balance` is `Money`, not `decimal`.
- Ledger-posting module extracted at Task 8/10 (deposit credit, transfer debit, payout debit
  share one implementation) — do not triplicate it.
- Webhook payload DTOs must match real Circle SNS envelope shapes (string amounts, nested
  `<resource>` object), not the invented flat DTOs in earlier snippets.
- `DepositSourceType` is `Wire | OnChain`. On-chain deposits arrive on the `transfers` topic, not
  `deposits`.
- Deposit-address generation needs a system-generated idempotency key (real Circle endpoint
  requires one) — reserve/complete pattern, not plain find-or-create alone.
- `Substitute.For<T>()` NSubstitute mocks are illustrative-nonsense in this ticket set — every
  handler test should be written directly against Moq per repo convention.

## Testing per tier (CLAUDE.md)

- Domain: xUnit v3, entity invariants/state machines.
- Application: xUnit v3 + Moq (mocked ports) + FluentAssertions, no real DbContext/HTTP.
- Api: WebApplicationFactory + real Testcontainers SQL Server, no in-memory EF provider.

## Definition of done (per ticket)

1. Red: write failing test(s) at the tier(s) the ticket touches, following the
   corrections above (Moq, real webhook shapes, `Money`, `TenantScope`).
2. Green: minimal implementation to pass.
3. `check.sh` clean (0 warnings/errors) on every touched project.
4. `test-fast.sh` green.
5. Ticket touching Api/webhook/DB: `test-full.sh` green (Testcontainers).
6. Ticket changing the OpenAPI-visible surface: `contract.sh` re-run, diff reviewed.
7. Migration involved: `schema.sh new` then hand-review the generated migration before `apply`
   (CLAUDE.md "Data is irreversible" — never a blind drop-and-add).
