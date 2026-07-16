# How minting and redemption works

> Understand how Circle Mint converts fiat to USDC (minting) and USDC back to fiat (redemption), including settlement timing, account structure, and compliance requirements.

Source: https://developers.circle.com/circle-mint/concepts/how-minting-works (verified live 2026-07-07)

Minting converts fiat currency into stablecoins (USDC or EURC); redemption
converts stablecoins back to fiat. Every token mints and redeems at a 1:1
ratio with the underlying fiat currency.

## Minting

1. Initiate a fiat transfer from a linked bank account to Circle.
2. Circle receives the deposit and credits the Mint account balance.
3. Stablecoins become available for onchain transfers or other operations.

Settlement: domestic wire before daily cutoff settles same business day;
real-time rails (RTP/SPEI/SEPA/CHATS) settle in seconds; international wires
take 1-3 business days. Sandbox mock wire deposits batch and may take up to
15 minutes.

## Redemption

1. Create a payout specifying amount and destination bank account
   (`POST /v1/businessAccount/payouts`).
2. Circle debits the stablecoin balance.
3. Circle sends fiat via the appropriate rail for the region/bank.

Settlement: typically next business day. Bank-side rejection produces a
returned withdrawal — funds re-credit to the Mint balance.

## Account structure

- **Primary wallet** — `masterWalletId`, retrieved from `/v1/configuration`.
  Source for outbound transfers, destination for inbound deposits.
- **Balances** — `available` (settled, spendable now) vs `unsettled`
  (in-transit, e.g. wire deposits before clearing).
- **Linked bank accounts** — each gets a unique Virtual Account Number (VAN)
  for wire attribution without a tracking reference.
- **Deposit addresses** — one per blockchain; receive inbound stablecoin
  transfers.
- **Recipient addresses** — external blockchain addresses registered/
  allowlisted before you can send to them.

## Onchain transfers

- Receiving: external wallet sends to your deposit address; Circle credits
  after required [blockchain confirmations](../reference/blockchain-confirmations.md).
- Sending: create a transfer to a registered recipient address; Circle debits
  and broadcasts.

Status lifecycle: `pending` (created, not broadcast) → `running` (broadcast,
awaiting confirmations) → `complete` (confirmed, final).

## Network fees

Circle covers gas fees for outbound stablecoin transfers in most cases — no
need to hold the chain's native token.

## Travel Rule compliance

Transfers ≥$3,000 on supported blockchains are subject to FinCEN Travel Rule
(identity data about the originator):

- **Business account transfers** (`POST /v1/businessAccount/transfers`) — Circle
  uses your company's identity on file; no per-request identity data needed.
- **Third-party payouts** (`POST /v1/payouts`) — you must provide the
  originator's identity (name + address) in the request. Omitting required
  data fails the transfer. See [Travel rule compliance](../reference/travel-rule-compliance.md).

## Approval workflows

Customers in France and Singapore require recipient address verification via
the Mint Console before an outbound transfer can proceed.
