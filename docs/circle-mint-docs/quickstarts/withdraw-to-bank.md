# Quickstart: Withdraw fiat to bank

> Redeem USDC or EURC to fiat and withdraw funds to your bank account using the Circle Mint API.

Source: https://developers.circle.com/circle-mint/quickstart-withdraw-to-bank

Redeem (offramp) USDC or EURC in your Circle Mint balance to fiat and send
funds to a linked bank account. Track payout status from `pending` through
`complete` or `failed`, and handle returned withdrawals caused by bank-side
rejections. Payouts route through the `/wires` endpoint and support standard
wires, real-time interbank rails (RTP, SPEI, SEPA, CHATS) where available, and
book transfers when applicable — Circle selects the rail based on your
destination bank and region.

## Prerequisites

- Complete [account and API key setup](getting-started.md).
- Have a funded Circle Mint account with available USDC or EURC balance.
- Have a linked bank account (see `Deposit Fiat` how-to, Step 1).
- If your Circle Mint account is domiciled in Singapore or France, verify
  payout recipients through the Mint Console before proceeding — unverified
  recipients cause payouts to remain in `pending`.

## Step 1. Verify your balance

```bash theme={null}
curl -X GET https://api-sandbox.circle.com/v1/businessAccount/balances \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json"
```

```json theme={null}
{
  "data": {
    "available": [{ "amount": "150.00", "currency": "USD" }],
    "unsettled": [{ "amount": "25.00", "currency": "USD" }]
  }
}
```

`available` is withdrawable now; `unsettled` is still processing.

## Step 2. Create a payout

`POST /v1/businessAccount/payouts` — create a payout, endpoint doc:
`/api-reference/circle-mint/account/create-business-payout`.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/payouts \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey": "'$(uuidgen)'", "destination": {"type": "wire", "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636"}, "amount": {"currency": "USD", "amount": "75.00"}}'
```

Response includes `id`, `status: "pending"`. Record the `id` to poll status.

## Step 3. Check payout status

`GET /v1/businessAccount/payouts/{id}` (`get-business-payout` endpoint).

Statuses: `pending` (processing) → `complete` (sent) or `failed` (check
`errorCode`).

Payouts are asynchronous — subscribe to `payouts` webhook notifications
instead of polling where possible.

### Returned withdrawals

Even a `complete` payout can be returned by the receiving bank: wrong routing
number/closed account, compliance hold, or beneficiary name mismatch. Returned
funds are re-credited to the Circle Mint balance. Watch `payouts` webhook
events to detect returns; retry with a new idempotency key after fixing the
bank details.

## See also

- How minting and redemption works — `/circle-mint/concepts/how-minting-works`
- Sandbox to production differences — `/circle-mint/references/sandbox-and-testing`
- Create a payout — `/api-reference/circle-mint/account/create-business-payout`
