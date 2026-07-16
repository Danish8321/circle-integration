# Quickstart: Mint and redeem USDC

> Deposit fiat to mint USDC, transfer it onchain, and redeem USDC back to fiat using the Circle Mint API.

Verified live 2026-07-16 at https://developers.circle.com/circle-mint/quickstarts/mint-and-redeem
(this file previously had no header citation; the filename-implied slug
`mint-and-redeem-usdc` 404s) — content matches.

This guide walks you through a complete mint-and-redeem cycle in the Circle Mint
sandbox: link a bank account, deposit fiat to mint USDC, transfer USDC onchain,
and redeem USDC back to fiat.

## Prerequisites

Before you begin, complete the
[account and API key setup](/circle-mint/quickstarts/getting-started).

Replace `${YOUR_API_KEY}` in the examples below with your sandbox API key.

## Step 1: Create a bank account

Register a mock bank account using the
[create a wire bank account](/api-reference/circle-mint/account/create-business-wire-account)
endpoint. This bank account serves as the source for depositing fiat and the
destination for redeeming USDC.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/banks/wires \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "unique-id-1",
    "accountNumber": "12340010",
    "routingNumber": "121000248",
    "billingDetails": {
      "name": "Satoshi Nakamoto",
      "city": "Boston",
      "country": "US",
      "line1": "100 Money Street",
      "district": "MA",
      "postalCode": "01234"
    },
    "bankAddress": {
      "bankName": "WELLS FARGO BANK, NA",
      "city": "San Francisco",
      "country": "US",
      "line1": "420 Montgomery Street",
      "district": "CA"
    }
  }'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636",
    "status": "pending",
    "description": "WELLS FARGO BANK, NA ****0010",
    "trackingRef": "CIR2GKYL4B",
    "fingerprint": "a9a71b77-d83d-4fbc-997f-41a33550c594",
    "virtualAccountEnabled": true,
    "billingDetails": {
      "name": "Satoshi Nakamoto",
      "line1": "100 Money Street",
      "city": "Boston",
      "postalCode": "01234",
      "district": "MA",
      "country": "US"
    },
    "bankAddress": {
      "bankName": "WELLS FARGO BANK, NA",
      "city": "SAN FRANCISCO",
      "district": "CA",
      "country": "US"
    },
    "createDate": "2026-01-15T12:00:00.000Z",
    "updateDate": "2026-01-15T12:00:00.000Z"
  }
}
```

Save the bank account `id` from the response. You need it in the following
steps.

## Step 2: Get wire instructions

Retrieve the wire instructions for your bank account using the
[get wire instructions](/api-reference/circle-mint/account/get-business-wire-account-instructions)
endpoint. The response includes the `trackingRef` and beneficiary
`accountNumber` you need to simulate a wire deposit.

```bash theme={null}
curl https://api-sandbox.circle.com/v1/businessAccount/banks/wires/9d1fa351-b24d-442a-8aa5-e717db1ed636/instructions \
  -H "Authorization: Bearer ${YOUR_API_KEY}"
```

Expected response:

```json theme={null}
{
  "data": {
    "trackingRef": "CIR2GKYL4B",
    "beneficiary": {
      "name": "CIRCLE INTERNET FINANCIAL INC",
      "address1": "99 High Street",
      "address2": "Boston, MA 02110"
    },
    "beneficiaryBank": {
      "name": "CUSTOMERS BANK",
      "routingNumber": "031101279",
      "accountNumber": "123815146304",
      "city": "Phoenixville",
      "postalCode": "19460",
      "country": "US"
    }
  }
}
```

Save the `trackingRef` and the `beneficiaryBank.accountNumber` values for the
next step.

## Step 3: Deposit fiat to mint USDC

Simulate a wire deposit using the
[create a mock wire payment](/api-reference/circle-mint/account/create-mock-wire-payment)
endpoint. In the sandbox, this mints USDC into your Circle Mint account without
moving real funds.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/mocks/payments/wire \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": { "amount": "100.00", "currency": "USD" },
    "trackingRef": "CIR2GKYL4B",
    "beneficiaryBank": { "accountNumber": "123815146304" }
  }'
```

Expected response:

```json theme={null}
{
  "data": {
    "trackingRef": "CIR2GKYL4B",
    "amount": { "amount": "100.00", "currency": "USD" },
    "status": "pending"
  }
}
```

<Note>
  Sandbox mock wire deposits process in batches and may take up to 15 minutes to
  complete. Wait for the deposit to settle before continuing.
</Note>

After the deposit settles, verify your balance using the
[list all balances](/api-reference/circle-mint/account/list-business-balances)
endpoint:

```bash theme={null}
curl https://api-sandbox.circle.com/v1/businessAccount/balances \
  -H "Authorization: Bearer ${YOUR_API_KEY}"
```

Expected response:

```json theme={null}
{
  "data": {
    "available": [{ "amount": "100.00", "currency": "USD" }],
    "unsettled": []
  }
}
```

The `available` balance confirms that your fiat deposit minted USDC
successfully.

## Step 4: Transfer USDC onchain

Send USDC from your Circle Mint account to an external blockchain address. This
step requires two API calls: create a recipient address, then create a transfer.

### 4.1. Create a recipient address

Register a destination address using the
[create a recipient address](/api-reference/circle-mint/account/create-business-recipient-address)
endpoint. This example uses the Ethereum blockchain.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/wallets/addresses/recipient \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "unique-id-2",
    "address": "0x493A9869E3B5f846f72267ab19B76e9bf99d51b1",
    "chain": "ETH",
    "currency": "USD",
    "description": "External Ethereum wallet"
  }'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "cfa01bb0-d166-5506-a48a-56f2beab559f",
    "address": "0x493a9869e3b5f846f72267ab19b76e9bf99d51b1",
    "chain": "ETH",
    "currency": "USD",
    "description": "External Ethereum wallet"
  }
}
```

### 4.2. Create a transfer

Send USDC to the recipient address using the
[create a transfer](/api-reference/circle-mint/account/create-business-transfer)
endpoint:

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/transfers \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "unique-id-3",
    "destination": {
      "type": "verified_blockchain",
      "addressId": "cfa01bb0-d166-5506-a48a-56f2beab559f"
    },
    "amount": { "currency": "USD", "amount": "25.00" }
  }'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "21fd4ec4-bad1-4eb2-9fc5-60320dedc7ea",
    "source": { "type": "wallet", "id": "1016875042" },
    "destination": {
      "type": "blockchain",
      "address": "0x493a9869e3b5f846f72267ab19b76e9bf99d51b1",
      "chain": "ETH"
    },
    "amount": { "amount": "25.00", "currency": "USD" },
    "status": "pending"
  }
}
```

The transfer starts in `pending` status, moves to `running` once broadcast
onchain, and reaches `complete` after enough
[blockchain confirmations](/circle-mint/references/blockchain-confirmations).

## Step 5: Redeem USDC to fiat

Convert your remaining USDC balance back to fiat by creating a payout to the
bank account you registered in Step 1. Use the
[create a payout](/api-reference/circle-mint/account/create-business-payout)
endpoint:

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/payouts \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "unique-id-4",
    "destination": {
      "type": "wire",
      "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636"
    },
    "amount": { "currency": "USD", "amount": "75.00" }
  }'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "9cf38c76-cac4-40d8-a516-f46e9a610a85",
    "amount": { "amount": "75.00", "currency": "USD" },
    "status": "pending",
    "sourceWalletId": "1016875042",
    "destination": {
      "type": "wire",
      "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636",
      "name": "WELLS FARGO BANK, NA ****0010"
    }
  }
}
```

## Step 6: Verify the round trip

Check your final balance to confirm both the transfer and payout processed:

```bash theme={null}
curl https://api-sandbox.circle.com/v1/businessAccount/balances \
  -H "Authorization: Bearer ${YOUR_API_KEY}"
```

Expected response:

```json theme={null}
{
  "data": {
    "available": [{ "amount": "0.00", "currency": "USD" }],
    "unsettled": []
  }
}
```

You completed the full Circle Mint cycle:

1. Deposited \$100 USD via wire to mint 100 USDC.
2. Transferred 25 USDC onchain to an Ethereum address.
3. Redeemed 75 USDC back to fiat via wire payout.

Your available balance returns to zero, confirming every dollar is accounted
for.

<Note>
  The mock wire endpoint used in Step 3 is only available in the sandbox. In
  production, initiate wire transfers from your own banking interface using the
  wire instructions from Step 2. All other API calls in this guide work the same
  in production. See [Sandbox to
  Production](/circle-mint/references/sandbox-and-testing) for details on
  transitioning.
</Note>
