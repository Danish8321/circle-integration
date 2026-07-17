# ADR 0003: TransactionType has no separate Mint value

**Status:** Accepted (2026-07-16)

## Decision

`TransactionType` enum is `Deposit | Transfer | Redemption` — there is no `Mint` value, despite PRD §9.2 phrasing "filter by type (deposit/transfer/redemption/mint)".

## Rationale

PRD §6.2 describes the fiat-wire funding path as: "the provider mints the equivalent USDC into the entity wallet." Minting is the *mechanism* by which a fiat-wire deposit settles, not a separate transaction the caller initiates or a distinct ledger event — both funding paths (fiat wire, on-chain USDC transfer) converge on the same webhook-driven credit and are recorded as `Deposit` (§6.2's sequence diagram: one webhook, one credit). Existing implementation already reflects this three-value enum; §9.2's parenthetical is PRD prose, not a contract commitment to a fourth type.

## Consequences

Don't add a `Mint` value to `TransactionType`. If a future requirement needs to distinguish fiat-wire-triggered deposits from on-chain-triggered deposits, that's a new field (e.g. `DepositSourceType`, which already exists per Task 8) — not a new `TransactionType`.
