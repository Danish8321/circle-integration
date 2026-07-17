Status: open

Source: `docs/features/04-ledger-and-balances.md` (old source `docs/Phase_1_Feature_Slices.md`
Task 8, deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 03-deposit-address-generation.

## Correction to original scope (ticket premise was stale, found 2026-07-17 grilling)

Original scope assumed a pre-existing `Domain/Deposit.cs` + `Application/Deposits/*` +
`Application/Ports/IDepositRepository.cs` to supersede/delete. A `find src -iname "*Deposit*"`
audit this session found **none of it exists** — there is no prior deposit code in `src/` to
delete, and no `Deposit` table to drop in the eventual migration. Ticket 03
(`03-deposit-address-generation`) is what introduces the *first* deposit-related code
(`DepositAddress` only, not `Transaction`/crediting). This ticket builds the PRD §9.1 ledger
fresh, not as a supersession.

## Scope

Builds the PRD §9.1 ledger fresh: every service-initiated transaction and every provider webhook
event produces/updates a `Transaction` row; `BalanceSnapshot` recorded per wallet on schedule and
after every ledger mutation. Wires the `deposits` webhook topic (currently unrouted, falls to
no-op `Ok()`). Adds list/get transaction + balance endpoints.

This is the anchor ticket for the shared ledger-posting module (design-pass #2) that Tasks
06/07 (this numbering: recipients/transfers/redemption) will also consume.

## Files (see Task 8 for exact list)

- New: `Domain/{TransactionType,TransactionStatus,Transaction,BalanceSnapshotReason,
  BalanceSnapshot}.cs`, `Application/Ledger/Ports/{ITransactionRepository,
  IBalanceSnapshotRepository}.cs`, `Application/Ledger/{ProcessDepositCommand,
  ProcessDepositResult,ProcessDepositCommandValidator,ProcessDepositCommandHandler,
  DepositSourceNotResolvedException,ListTransactionsQuery,ListTransactionsQueryHandler,
  GetTransactionQuery,GetTransactionQueryHandler,GetCurrentBalanceQuery,
  GetCurrentBalanceQueryHandler,GetBalanceHistoryQuery,GetBalanceHistoryQueryHandler}.cs`,
  `Infrastructure/Persistence/{TransactionRepository,BalanceSnapshotRepository}.cs`,
  `Api/Ledger/TransactionsController.cs` (module-scoped path, matching the existing
  `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`; corrected 2026-07-17).
- Modify: `IDepositAddressRepository`/`DepositAddressRepository` (add `FindByAddressAsync`),
  `CircleWebhooksController`, `DbContext`.

## Key corrections that apply

- Design-pass #1: `FundAccount.Balance` fixed to `Money`, not `decimal` — this is where that
  fix lands.
- Design-pass #2: extract the ledger-posting module now (post-`Transaction` + adjust
  `FundAccount` balance + `BalanceSnapshot` as one implementation) — Tasks 06/07 (Transfers,
  Redemption) consume it, do not duplicate.
- Correction #5: `deposits` webhook = fiat wire only (`DepositSourceType.Wire`); on-chain
  deposits arrive on `transfers` topic (handled in 06-outbound-transfers ticket, branch on
  direction). Don't invent a `sourceType` discriminator on the deposits payload.
- Correction #8: `DepositSourceType` members are `Wire | OnChain`.
- Correction #7: real SNS envelope shapes for the `deposits` payload.

## Definition of done

- Domain tests: `Transaction`/`BalanceSnapshot` invariants.
- Application tests (Moq): `ProcessDepositCommandHandlerTests` — two-`SaveChangesAsync`
  pattern, ledger-posting module unit-tested in isolation.
- Api tests: transaction/balance endpoints, `deposits` webhook routing.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run.
- Migration hand-reviewed before `schema.sh apply` — this is an additive migration (new
  `Transaction`/`BalanceSnapshot` tables), not a drop; no `Deposit` table exists to delete
  (§ correction above).

## Comments
