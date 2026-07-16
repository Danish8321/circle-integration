# How-to: Receive a stablecoin payin

> Accept an onchain USDC or EURC payin into your Circle Mint account by creating a payment intent, sharing the deposit address, and confirming the payment.

Source: https://developers.circle.com/circle-mint/crypto-payments-quickstart
(nav label: "Quickstart: Crypto Deposits — Receive USDC"; canonical how-to path
is under `/cpn/stablecoin-payments/howtos/receive-stablecoin-payin`)

Use the Stablecoin Payins API to accept an onchain USDC or EURC payment into
your Circle Mint account: create a payment intent, share the deposit address
Circle assigns, then confirm the payment after the customer transfers funds.

## Prerequisites

- Stablecoin Payins enabled on your Circle Mint account (Americas/Circle LLC
  scope only — contact Circle to request access).
- A Circle Mint sandbox API key (see [Getting started](../quickstarts/getting-started.md)).
- A blockchain supported by the Payins API — narrower than the Payouts API.
- A `merchantWalletId` for the wallet that receives settled funds (no funded
  Mint account needed — this is a receive flow).

## Steps

### 1. Create a payment intent

`POST /v1/paymentIntents`. Default mode is **continuous** (repeatable
deposits); use `type: "transient"` with an `amount` for a fixed-amount,
single-checkout, single-use address that expires at `expiresOn`.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/paymentIntents \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "17607606-e383-4874-87c3-7e46a5dc03dd",
    "currency": "USD",
    "settlementCurrency": "USD",
    "merchantWalletId": "1000999922",
    "paymentMethods": [{ "type": "blockchain", "chain": "BASE" }]
  }'
```

Response includes intent `id` and a `timeline` at `created`; deposit address
not yet populated.

### 2. Get the deposit address

Circle assigns the address asynchronously. Either subscribe to `paymentIntents`
webhook notifications (address appears in `paymentMethods[0].address`,
timeline advances to `pending`), or poll `GET /v1/paymentIntents/{id}` until
the address appears.

Memo-based chains (Stellar XLM, Hedera HBAR) also return an `addressTag` —
customers must include it or funds may not be credited correctly.

### 3. Customer transfers funds

Display the deposit address (and `addressTag` if applicable). The customer
sends USDC/EURC from their own wallet — outside the API.

**Funds sent on the wrong blockchain are permanently lost** — the address is
only valid on the chain assigned to the intent.

### 4. Confirm the payment

- **Continuous intents**: stay at `active` forever; timeline never reaches
  `complete`; `paymentIds` stays empty. Reconcile each settled transfer via the
  `payments` webhook or `GET /v1/payments?paymentIntentId={id}`.
- **Transient intents**: transition to `complete` once the single expected
  transfer settles. Latest timeline entry carries `context`: `paid`,
  `underpaid`, or `overpaid`. `paymentIds` lists the settled payment(s).

A `payments` webhook notification fires on every settled inbound transfer for
either mode, carrying `status`, `fromAddresses`, `transactionHash`, and the
linking `paymentIntentId`.

### 5. (Optional) Expire a transient intent

`POST /v1/paymentIntents/{id}/expire` — transient intents only. Funds sent to
an already-expired address are still credited to the Circle Mint account but
won't match the original intent; contact Circle Support to reconcile.

## Completion contexts (transient intents only)

| Context     | Meaning                                   | Typical handling                                    |
| ----------- | ------------------------------------------ | ---------------------------------------------------- |
| `paid`      | `amountPaid` equals `amount`               | Fulfill the order.                                    |
| `underpaid` | Customer paid less than expected `amount`  | Refund partial payment, or ask customer to top up.    |
| `overpaid`  | Customer paid more than expected           | Refund excess, or refund full amount and ask to retry.|

Continuous intents never produce a completion context — there's no fixed
target amount to compare against.

## See also

- Stablecoin payins and payouts concepts — `/cpn/stablecoin-payments/concepts/how-stablecoin-payments-work`
- Refund a stablecoin payin — `/cpn/stablecoin-payments/howtos/refund-stablecoin-payin`
- Supported chains — `/cpn/stablecoin-payments/references/supported-blockchains`
- Set up a webhook endpoint — `/api-reference/webhook-endpoints#v1-notifications`
