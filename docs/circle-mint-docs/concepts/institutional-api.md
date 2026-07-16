# Institutional API

> Conceptual model for Circle Mint Distributors operating fiat-to-stablecoin flows on behalf of compliance-screened institutional end clients.

Source: https://developers.circle.com/circle-mint/concepts/institutional-api (verified live 2026-07-07)

The Institutional API lets a Circle Mint Distributor mint, redeem, and
transfer stablecoins on behalf of institutional end clients (External
Entities), each with a dedicated wallet.

## Roles

- **Distributor** — the Circle Mint customer holding the API key.
- **External Entity** — the Distributor's institutional client; never
  accesses Circle APIs directly.

## Compliance

Creating an External Entity (`POST /v1/externalEntities`) triggers synchronous
sanctions screening → `complianceState: PENDING`. Final `ACCEPTED`/`REJECTED`
arrives asynchronously via the `externalEntities` webhook topic.

## Wallet model

Accepted entities get a dedicated `end_user_wallet` subaccount (`walletId`).
Balances are segregated per counterparty, still under the Distributor's
account.

## Supported operations

Mint, redeem (flat-fee deducted — Institutional Direct billing), and onchain
transfer, all scoped by the entity's `walletId`. See
[Manage institutional subaccounts](../howtos/manage-institutional-subaccounts.md)
for the full step-by-step flow and endpoint table.

## Not supported

Editing/deleting entities, express routes, local-currency swaps, Stablecoin
Payins/Payouts, or Credit API operations for external entities.
