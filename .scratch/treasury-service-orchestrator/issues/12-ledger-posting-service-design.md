Status: open

Source: `docs/features/04-ledger-and-balances.md` §7 (open items log); `docs/README.md` §7.
Blocked by: 04-ledger-transaction-and-balance.

## Scope

Two unresolved design points inside the ledger module's own shipped/planned surface:

1. **`LedgerPostingService`'s exact shape was undocumented in source material and had to be
   synthesized** during the doc-restructure pass — flagged for review, not treated as settled.
   Needs an implementer (or a design pass) to confirm the synthesized shape (method signature,
   idempotency handling, transaction boundary) actually matches what `04-ledger-and-balances.md`
   assumes elsewhere before code is built against it.
2. **`GetCurrentBalanceQueryHandler`'s `Money.Zero("USD")` default vs. a funded account's
   `USDC` currency** — an unfunded `FundAccount` currently defaults its balance read to `USD`,
   but every funded account in this codebase is `USDC`-denominated. Open product question: is
   the zero-balance default supposed to be `USDC` too (consistent with everything else), or is
   `USD` intentional for some pre-funding state this doc pass didn't uncover a source for?

## Definition of done

- `LedgerPostingService`'s shape either confirmed against a located source, or explicitly
  ratified as a fresh design decision (record which, and by whom/when, here).
- `Money.Zero` currency question answered and, if changed, `GetCurrentBalanceQueryHandlerTests`
  updated to assert the corrected default.

## Comments
