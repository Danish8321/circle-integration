Status: resolved

Source: `docs/features/04-ledger-and-balances.md` §6 (open items log); `docs/README.md` §7.
Blocked by: 04-ledger-transaction-and-balance.

## Scope (resolved 2026-07-17 grilling)

Two design points, both ratified — see `docs/features/04-ledger-and-balances.md` §6 for the
recorded reasoning:

1. **`LedgerPostingService.PostAsync` signature ratified**: single method, signed `Money.Amount`
   (credit = positive, debit = negative), not split `CreditAsync`/`DebitAsync`. Matches the
   source constraint's "stays one method" wording.
2. **`GetCurrentBalanceQueryHandler`'s zero-balance default ratified**: `Money.Zero("USDC")`, not
   `Money.Zero("USD")` — every funded `FundAccount` in this stablecoin-only product is USDC, so
   the no-activity default now matches.

## Definition of done

- [x] `LedgerPostingService`'s shape ratified as a fresh design decision (this session,
      2026-07-17).
- [x] `Money.Zero` currency question answered — `USDC`. `docs/features/04-ledger-and-balances.md`
      §4.1/§5/§6 updated; ticket 04's own `GetCurrentBalanceQueryHandlerTests` definition-of-done
      line carries the corrected default forward.

## Comments
