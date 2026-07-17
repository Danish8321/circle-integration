# Manage institutional subaccounts

> Step-by-step guide for Circle Mint distributors to create external entities, mint and redeem on their behalf, transfer USDC onchain, and query entity-scoped activity.

Verified live against https://developers.circle.com/circle-mint/howtos/manage-institutional-subaccounts on 2026-07-07 — content below unchanged.

Operate fiat-to-stablecoin flows on behalf of an external entity end-to-end:
onboard the entity, wait for the compliance decision, then mint, redeem, and
transfer USDC onchain on its behalf using the entity's dedicated `walletId`. Use
this guide when you hold the Institutional API entitlement as a Distributor and
you're integrating a new institutional counterparty into your Circle Mint
account. For the conceptual model, see
[Institutional API](/circle-mint/concepts/institutional-api).

## Prerequisites

Before you begin, ensure that you've:

* Confirmed the Institutional API entitlement is enabled on your Circle Mint
  account. [Contact Circle](https://www.circle.com/mint-contact) if you don't
  see it.
* Created an API key with institutional permissions.
* Set up a webhook subscription that includes the `externalEntities`,
  `deposits`, `transfers`, and `payouts` topics. See
  [Set up a webhook endpoint](/api-reference/webhook-endpoints#v1-notifications).
* Verified at least one linked bank account for receiving wires from your end
  client. See [Depositing Fiat](/circle-mint/howtos/deposit-fiat).
* Reviewed the conceptual model in
  [Institutional API](/circle-mint/concepts/institutional-api).

## Step 1: Create an external entity

Call `POST /v1/externalEntities` with the entity's `businessName`,
`businessUniqueIdentifier` (tax ID), `identifierIssuingCountryCode`, and
`address`.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/externalEntities \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "businessName": "Acme Treasury Ltd.",
    "businessUniqueIdentifier": "12-3456789",
    "identifierIssuingCountryCode": "US",
    "address": {
      "country": "US",
      "state": "CA",
      "city": "San Francisco",
      "postalCode": "94105",
      "streetName": "Market St",
      "buildingNumber": "100"
    }
  }'
```

<Note>
  **Corrected 2026-07-16** against the live API reference
  (`api-reference/circle-mint/institutional/create-external-entity`): the
  `address` object's fields are `country`, `state`, `city`, `postalCode`,
  and optional `streetName`/`buildingNumber` — not `line1`/`district` as this
  page previously showed. `docs/Phase_1_Feature_Slices.md` Task 3 already used
  the correct field names; only this example was stale.
</Note>

The response returns HTTP 201 with `complianceState: PENDING`:

```json theme={null}
{
  "data": {
    "walletId": "212000",
    "businessName": "Acme Treasury Ltd.",
    "businessUniqueIdentifier": "12-3456789",
    "identifierIssuingCountryCode": "US",
    "complianceState": "PENDING"
  }
}
```

<Note>
  The `walletId` is returned at creation but remains unusable while the entity
  is in `PENDING` or `REJECTED`. Wait for the compliance decision before
  referencing the entity's wallet in any other endpoint.
</Note>

## Step 2: Wait for the compliance decision

Subscribe to the `externalEntities` webhook topic to receive the asynchronous
compliance decision. On `ACCEPTED`, the payload confirms the entity's `walletId`
is usable. On `REJECTED`, the entity cannot be used: resubmit a new entity with
corrected information or [contact Circle](https://www.circle.com/mint-contact).
As a fallback, poll `GET /v1/externalEntities/{walletId}` with the `walletId`
returned at creation until `complianceState` changes.

```json theme={null}
{
  "clientId": "a03a47ff-b0eb-4070-b3df-dc66752cc802",
  "notificationType": "externalEntities",
  "version": 1,
  "externalEntity": {
    "walletId": "212000",
    "businessName": "Acme Treasury Ltd.",
    "businessUniqueIdentifier": "12-3456789",
    "identifierIssuingCountryCode": "US",
    "complianceState": "ACCEPTED"
  }
}
```

## Step 3: Mint on behalf of the entity

Use the entity's `walletId` to scope wire instructions, then watch for the
deposit to land on the entity wallet.

### 3.1. Generate entity-scoped wire instructions

Call
`GET /v1/businessAccount/banks/wires/{id}/instructions?walletId=<entity walletId>`,
passing the linked bank `id` in the path and the entity wallet in the query
string.

```bash theme={null}
curl "https://api-sandbox.circle.com/v1/businessAccount/banks/wires/9d1fa351-b24d-442a-8aa5-e717db1ed636/instructions?walletId=212000" \
  -H "Authorization: Bearer $API_KEY"
```

The response returns the entity-scoped `trackingRef`; deposits that include it
are credited to the entity wallet:

```json theme={null}
{
  "data": {
    "trackingRef": "CIR22FEP33",
    "beneficiary": {
      "name": "CIRCLE INTERNET FINANCIAL INC",
      "address1": "1 MAIN STREET",
      "address2": "SUITE 1"
    },
    "virtualAccountEnabled": true,
    "beneficiaryBank": {
      "name": "CRYPTO BANK",
      "address": "1 MONEY STREET",
      "city": "NEW YORK",
      "postalCode": "1001",
      "country": "US",
      "swiftCode": "CRYPTO99",
      "routingNumber": "999999999",
      "accountNumber": "123815146304",
      "currency": "USD"
    }
  }
}
```

### 3.2. Have the end client wire fiat to those instructions

Share the beneficiary details and the entity-scoped `trackingRef` with your end
client. Optionally include a `customerExternalRef` matching
`.*(EXT[A-Z0-9]{18}).*` in the bank memo for Distributor-side reconciliation.

### 3.3. Watch the `deposits` webhook for the credit

Circle fires a `deposits` event with `destination.id` equal to the entity's
`walletId` once the wire settles. The USDC or EURC is then available in the
entity wallet.

```json theme={null}
{
  "clientId": "a03a47ff-b0eb-4070-b3df-dc66752cc802",
  "notificationType": "deposits",
  "version": 1,
  "deposit": {
    "id": "b8627ae8-732b-4d25-b947-1df8f4007a29",
    "sourceWalletId": "212000",
    "destination": {
      "type": "wallet",
      "id": "212000"
    },
    "amount": {
      "amount": "50000.00",
      "currency": "USD"
    },
    "status": "complete",
    "trackingRef": "CIR22FEP33",
    "createDate": "2026-05-01T14:20:30.000Z",
    "updateDate": "2026-05-01T14:21:12.000Z"
  }
}
```

## Step 4: Transfer USDC onchain on behalf of the entity

Move USDC into or out of the entity wallet onchain by scoping the deposit
address and the transfer source to the entity `walletId`.

### 4.1. Generate an entity-scoped deposit address for inbound transfers

Call `POST /v1/businessAccount/wallets/addresses/deposit` with the entity wallet
in the request body. Prefer Arc when the entity supports it: Mint supports the
same blockchains for institutional subaccount wallets as for the Distributor's
primary wallet. See
[Supported Chains and Currencies](/circle-mint/references/supported-chains-and-currencies).

```bash theme={null}
curl -X POST "https://api-sandbox.circle.com/v1/businessAccount/wallets/addresses/deposit?walletId=212000" \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "idempotencyKey": "9352ec9e-5ee6-441f-ab42-186bc71fbdde",
    "currency": "USD",
    "chain": "ARC"
  }'
```

<Note>
  **Corrected 2026-07-17** against the live API reference
  (`api-reference/circle-mint/account/create-business-deposit-address`): `walletId`
  is a **query parameter**, not a body field (this page previously showed it in the
  body), the body requires `idempotencyKey`, and omitting `walletId` generates the
  address on the Distributor's main wallet.
</Note>

### 4.2. Send an outbound transfer from the entity wallet

First, allowlist the recipient with
`POST /v1/businessAccount/wallets/addresses/recipient`. The address must be
approved by an account administrator through the Mint Console before it can be
used. Watch the
[`addressBookRecipients`](/circle-mint/references/webhook-notifications#addressbookrecipients)
webhook for the `active` status that signals approval. Then create the transfer
with `POST /v1/businessAccount/transfers`, setting `source.type` to `wallet` and
`source.id` to the entity's `walletId`.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/transfers \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "source": { "type": "wallet", "id": "212000" },
    "destination": { "type": "verified_blockchain", "addressId": "addr_01HZ..." },
    "amount": { "amount": "1000.00", "currency": "USD" },
    "idempotencyKey": "9352ec9e-5ee6-441f-ab42-186bc71fbdde"
  }'
```

## Step 5: Redeem on behalf of the entity

Call `POST /v1/businessAccount/payouts` with `source.type` set to `wallet`,
`source.id` set to the entity's `walletId`, and `destination.type` set to `wire`
with the linked fiat account `id`. Watch the `payouts` webhook for `complete` or
`failed`.

```bash theme={null}
curl -X POST https://api-sandbox.circle.com/v1/businessAccount/payouts \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "source": { "type": "wallet", "id": "212000" },
    "destination": { "type": "wire", "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636" },
    "amount": { "amount": "10000.00", "currency": "USD" },
    "idempotencyKey": "ba943ff1-ca16-49b2-ba55-1057e70ca5c7"
  }'
```

The Institutional Direct fee is deducted at the point of redemption, so the
`toAmount` returned on the payout reflects the net amount the entity's bank
receives:

```json theme={null}
{
  "clientId": "a03a47ff-b0eb-4070-b3df-dc66752cc802",
  "notificationType": "payouts",
  "version": 1,
  "payout": {
    "id": "c0f88a17-2a8b-4d51-9c4e-c8d3f2cfa011",
    "sourceWalletId": "212000",
    "destination": {
      "type": "wire",
      "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636"
    },
    "amount": {
      "amount": "10000.00",
      "currency": "USD"
    },
    "toAmount": {
      "amount": "9990.00",
      "currency": "USD"
    },
    "fees": {
      "amount": "10.00",
      "currency": "USD"
    },
    "status": "complete",
    "createDate": "2026-05-01T14:20:30.000Z",
    "updateDate": "2026-05-01T14:25:11.000Z"
  }
}
```

## Step 6: Query entity-scoped activity

Filter list endpoints by the entity's `walletId` (or `sourceWalletId` /
`destinationWalletId` where supported) to retrieve activity scoped to a single
entity:

* Balance: `GET /v1/businessAccount/balances?walletId=<entity walletId>`
* Deposits: `GET /v1/businessAccount/deposits?walletId=<entity walletId>`
* Transfers: `GET /v1/businessAccount/transfers?walletId=<entity walletId>`. Use
  `sourceWalletId` or `destinationWalletId` to filter by direction.
* Payouts: `GET /v1/businessAccount/payouts?sourceWalletId=<entity walletId>`
* Deposit addresses:
  `GET /v1/businessAccount/wallets/addresses/deposit?walletId=<entity walletId>`

<Tip>
  Omitting `walletId` on these endpoints returns activity for the Distributor's
  primary wallet (`masterWalletId`), not all entities. Always pass the entity
  `walletId` when you want entity-scoped results.
</Tip>

## Endpoint reference

The following table maps each operation in this guide to its endpoint and the
location of the `walletId` parameter.

| Operation                       | Method and path                                                    | `walletId` location                            |
| ------------------------------- | ------------------------------------------------------------------ | ---------------------------------------------- |
| Create external entity          | `POST /v1/externalEntities`                                        | Body                                           |
| List external entities          | `GET /v1/externalEntities`                                         | Optional filter via `businessUniqueIdentifier` |
| Get external entity             | `GET /v1/externalEntities/{walletId}`                              | Path parameter                                 |
| Get entity wire instructions    | `GET /v1/businessAccount/banks/wires/{id}/instructions?walletId=…` | Query string                                   |
| Generate entity deposit address | `POST /v1/businessAccount/wallets/addresses/deposit`               | Body (`walletId`)                              |
| List entity deposit addresses   | `GET /v1/businessAccount/wallets/addresses/deposit?walletId=…`     | Query string                                   |
| Transfer onchain from entity    | `POST /v1/businessAccount/transfers`                               | Body (`source.id` with `source.type: wallet`)  |
| List entity transfers           | `GET /v1/businessAccount/transfers?walletId=…`                     | Query string                                   |
| Redeem from entity              | `POST /v1/businessAccount/payouts`                                 | Body (`source.id` with `source.type: wallet`)  |
| List entity payouts             | `GET /v1/businessAccount/payouts?sourceWalletId=…`                 | Query string                                   |
| List entity deposits            | `GET /v1/businessAccount/deposits?walletId=…`                      | Query string                                   |
| Entity balance                  | `GET /v1/businessAccount/balances?walletId=…`                      | Query string                                   |

## See also

* [Institutional API](/circle-mint/concepts/institutional-api): conceptual model
  for Distributors, external entities, and per-entity wallets.
* [Webhook notifications](/circle-mint/references/webhook-notifications#externalentities):
  schema and delivery for the `externalEntities` callback.
* [Error codes](/circle-mint/references/error-codes): synchronous and
  asynchronous failure modes.
* [Supported payment rails](/circle-mint/references/supported-payment-rails):
  fiat rails available for entity wire deposits and redemptions.
* [Depositing Fiat](/circle-mint/howtos/deposit-fiat): linked bank account setup
  and wire deposit basics.
