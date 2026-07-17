# ADR 0004: "Master Account" is the canonical term

**Status:** Accepted (2026-07-16, resolved via grilling)

## Decision

**Master Account** is the one ubiquitous-language term for the non-tenant-keyed, top-level Circle account. PRD's "Main (Distributor) account" and "Circle Mint primary wallet" are the same concept, not alternate terms to keep in rotation.

## Rationale

The PRD uses three phrasings for one concept (§2.5 "Main (Distributor) account", "Circle Mint primary wallet"; endpoint prefix `/master-account/...`). Left unresolved, this invites drift — new code or docs picking whichever phrase reads best locally. "Master Account" was chosen because it already matches the shipped endpoint naming (`GET /master-account/balances`, `/master-account/summary`, etc.), so no route/contract renaming is needed to align.

## Consequences

Use "Master Account" in all new code, docs, and API-facing text. "Distributor account" and "primary wallet" may still appear when directly quoting Circle's own documentation or the PRD verbatim, but new product-facing language should say Master Account.
