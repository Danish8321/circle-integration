# How-to: Transfer USDC onchain

> Receive USDC and EURC via deposit addresses and send them to external blockchain wallets using the Circle Mint API.

Verified live 2026-07-16 at https://developers.circle.com/circle-mint/howtos/transfer-on-chain
(this file previously had no header citation) — content matches.

Receive USDC or EURC by generating deposit addresses for external wallets to
send to, or send USDC or EURC to allowlisted recipient addresses on supported
blockchains. Third-party payouts may require Travel Rule compliance data; see
[Travel rule compliance](/circle-mint/references/travel-rule-compliance) for
thresholds, schemas, and failure modes.

## Prerequisites

Before you begin:

* Complete the
  [account and API key setup](/circle-mint/quickstarts/getting-started).
* Review
  [supported chains and currencies](/circle-mint/references/supported-chains-and-currencies)
  for available blockchains.
* (For sending) Have a funded Circle Mint account.
* (For sending) All recipient addresses must be approved by an account
  administrator through the [Mint Console](https://app.circle.com/signin) before
  you can create transfers. If your account is domiciled in France or Singapore,
  addresses require additional verification through the Mint Console.

## Step 1. Receive USDC via deposit address

### Step 1.1. Create a deposit address

Use the create deposit address endpoint to generate an address for receiving
USDC or EURC on a specific blockchain.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/wallets/addresses/deposit \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey": "ba943ff1-ca16-49b2-ba55-1057e70ca5c7", "currency": "USD", "chain": "ARC"}'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "d51d72d2-9955-4340-b3fd-2f07a82a1e6c",
    "address": "0xbd01242af414961c25aa72dcae06646fc52e9b92",
    "currency": "USD",
    "chain": "ARC"
  }
}
```

<Note>
  You can create one deposit address per blockchain. Use the same address for
  all deposits on that blockchain.
</Note>

### Step 1.2. Send funds to your deposit address

This step happens outside the Circle Mint API. The sender transfers USDC or EURC
from their external wallet to your deposit address on the matching blockchain.

<Warning>
  Sending funds on the wrong blockchain results in permanent loss. Always
  confirm the blockchain network matches between the sender's wallet and your
  deposit address.
</Warning>

### Step 1.3. Verify the deposit

Use the
[list transfers](/api-reference/circle-mint/account/list-business-transfers)
endpoint to confirm the incoming transfer has settled.

```bash theme={null}
curl https://api-sandbox.circle.com/v1/businessAccount/transfers \
  -H "Authorization: Bearer ${YOUR_API_KEY}"
```

Expected response:

```json theme={null}
{
  "data": [
    {
      "id": "a6a1b575-13d5-4e73-9da7-73e2a3e4418a",
      "source": {
        "type": "blockchain",
        "chain": "ARC"
      },
      "destination": {
        "type": "wallet",
        "id": "1000066041"
      },
      "amount": {
        "amount": "100.00",
        "currency": "USD"
      },
      "transactionHash": "0x4cfd25b5ab46e9fe25e845e7a7e0ea2f1f7e4bba3c6e0f1db0b846e4a1bc5fd2",
      "status": "complete",
      "createDate": "2024-01-01T12:00:00.000Z"
    }
  ]
}
```

The transfer reaches `complete` status after the required number of
[blockchain confirmations](/circle-mint/references/blockchain-confirmations) for
the deposit's blockchain.

## Step 2. Send USDC to an external address

### Step 2.1. Add a recipient address

Use the create recipient address endpoint to allowlist an external blockchain
address for outbound transfers.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/wallets/addresses/recipient \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey": "2a308497-e66e-4c42-ac1e-7bedab86d958", "address": "0x493A9869E3B5f846f72267ab19B76e9bf99d51b1", "chain": "ARC", "currency": "USD", "description": "Treasury wallet"}'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "cfa01bb0-d166-5506-a48a-56f2beab559f",
    "address": "0x493a9869e3b5f846f72267ab19b76e9bf99d51b1",
    "chain": "ARC",
    "currency": "USD",
    "description": "Treasury wallet"
  }
}
```

Adding a recipient address through the API creates a pending request. An account
administrator must approve the address through the
[Mint Console](https://app.circle.com/signin) before you can send transfers to
it. A confirmation notification is sent to all administrators.

### Step 2.2. Create a transfer

Use the create transfer endpoint to send funds to the approved recipient
address.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/transfers \
  -H "Authorization: Bearer ${YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"idempotencyKey": "6ec3827d-15bb-442e-9d4c-32e73e61cbf4", "destination": {"type": "verified_blockchain", "addressId": "cfa01bb0-d166-5506-a48a-56f2beab559f"}, "amount": {"currency": "USD", "amount": "25.00"}}'
```

Expected response:

```json theme={null}
{
  "data": {
    "id": "21fd4ec4-bad1-4eb2-9fc5-60320dedc7ea",
    "source": {
      "type": "wallet",
      "id": "1016875042"
    },
    "destination": {
      "type": "blockchain",
      "address": "0x493a9869e3b5f846f72267ab19b76e9bf99d51b1",
      "chain": "ARC"
    },
    "amount": {
      "amount": "25.00",
      "currency": "USD"
    },
    "status": "pending",
    "createDate": "2024-07-15T16:41:12.395Z"
  }
}
```

### Step 2.3. Check the transfer status

Use the get transfer endpoint to monitor the status of your transfer.

```bash theme={null}
curl -X GET https://api-sandbox.circle.com/v1/businessAccount/transfers/21fd4ec4-bad1-4eb2-9fc5-60320dedc7ea \
  -H "Authorization: Bearer ${YOUR_API_KEY}"
```

The transfer progresses through these statuses:

* **`pending`**: Circle received the request.
* **`running`**: The transaction is broadcast onchain. The response includes a
  `transactionHash`.
* **`complete`**: Blockchain finality reached -- the required number of
  confirmations passed.

Expected response for a running transfer:

```json theme={null}
{
  "data": {
    "id": "21fd4ec4-bad1-4eb2-9fc5-60320dedc7ea",
    "source": {
      "type": "wallet",
      "id": "1016875042"
    },
    "destination": {
      "type": "blockchain",
      "address": "0x493a9869e3b5f846f72267ab19b76e9bf99d51b1",
      "chain": "ARC"
    },
    "amount": {
      "amount": "25.00",
      "currency": "USD"
    },
    "transactionHash": "0x0654eee4f609f9c35e376cef9455dd9fc1546c482c5c32c8f8d434ead14fcf97",
    "status": "running",
    "createDate": "2024-07-15T16:41:12.395Z"
  }
}
```

## See also

* [How minting and redemption works](/circle-mint/concepts/how-minting-works):
  understand the transfer lifecycle
* [Travel rule compliance](/circle-mint/references/travel-rule-compliance):
  thresholds, schemas, and failure modes for third-party payouts
* [Blockchain confirmations](/circle-mint/references/blockchain-confirmations):
  confirmation counts by blockchain
* [Supported Chains and Currencies](/circle-mint/references/supported-chains-and-currencies):
  available blockchains
