# ADR 0002: FundAccount is a distinct entity from Wallet

**Status:** Accepted (2026-07-16, resolved via grilling)

## Decision

`FundAccount` is a distinct local entity from `Wallet`, not a synonym or renaming target.

- **Wallet** — the provider-side segregated wallet (`walletId`), created by the provider on registration acceptance. Holds provider identity/metadata.
- **FundAccount** — the local balance-holding entity, 1:1 with a `Wallet`, that the ledger (`Transaction`, `BalanceSnapshot`) mutates directly.

## Rationale

PRD §3.1 only names `Wallet`, but implementation (`IFundAccountRepository`, `FundAccount`) introduced a separate local entity without updating the glossary. Keeping them distinct matches PRD §9.1's "the service therefore owns a local ledger" — balance state that the service tracks independently of the provider's own (non-history-having) balance endpoint. Collapsing them would blur the provider-vs-local-ledger split that reconciliation (§11.4) depends on.

## Consequences

Glossary (`CONTEXT.md`) documents both terms. Future PRD or design-doc language should say "Wallet" only for the provider-side record and "FundAccount" for the local balance holder — don't use them interchangeably.
