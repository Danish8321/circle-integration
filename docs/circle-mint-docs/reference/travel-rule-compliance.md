# Travel rule compliance

> Reference for Travel Rule thresholds, schemas, payment reason codes, Virtual Asset Service Provider lookup, and failure modes that apply to Circle Mint Stablecoin Payouts.

Verified live against https://developers.circle.com/circle-mint/references/travel-rule-compliance on 2026-07-07 — content below unchanged. `POST /v1/payouts` (this page's endpoint) confirmed as the real, current crypto/stablecoin payout path — distinct from `POST /v1/businessAccount/payouts` (fiat wire redemption, no Travel Rule fields).

Travel Rule is a financial-crime regulation that requires financial institutions
to exchange originator and beneficiary information on cross-counterparty fund
transfers that exceed defined thresholds. The Financial Crimes Enforcement
Network (FinCEN) sets the rule in the United States, the Monetary Authority of
Singapore (MAS) sets the equivalent rule in Singapore under Notice PSN02, and
the European Union sets it under the Transfer of Funds Regulation (Regulation
(EU) 2023/1113), which applies to payouts booked through Circle SAS
(`CIRCLE_FR`).

This reference describes how Circle applies these rules to Stablecoin Payouts
and the third-party transfers booked through the Circle Mint Core API. Travel
Rule does not currently impose additional data requirements on Stablecoin
Payins.

## Regional rules

The Circle entity that books the payout determines which rule set applies. The
location of the customer or recipient is not the trigger.

| Circle entity                  | Trigger                                                                      | Threshold                     | Notes                                                                                                                                                                                                                                                                                                                                            |
| ------------------------------ | ---------------------------------------------------------------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Circle LLC (United States)     | All third-party payouts on Travel Rule blockchains at or exceeding threshold | \$3,000 USD-equivalent        | FinCEN. Originator identities required.                                                                                                                                                                                                                                                                                                          |
| Circle Singapore (`CIRCLE_SG`) | Every third-party payout                                                     | None. Applies to all amounts. | MAS PSN02. Originator identities, beneficiary identity, ownership, Virtual Asset Service Provider (VASP), and payment reason code all required. See [Ownership](#ownership-circle_sg-and-circle_fr) for the current self-hosted-wallet limitation.                                                                                               |
| Circle SAS (`CIRCLE_FR`)       | Every third-party payout                                                     | None. Applies to all amounts. | EU Transfer of Funds Regulation (Regulation (EU) 2023/1113). Originator identities, beneficiary identity, ownership, and Virtual Asset Service Provider (VASP) required. Beneficiaries can include an optional legal entity identifier (LEI). See [Ownership](#ownership-circle_sg-and-circle_fr) for the current self-hosted-wallet limitation. |

The booking entity on your account, not the geography of either side of the
transfer, decides which threshold and which fields apply.

## Schemas

The fields below describe the data Circle collects to satisfy Travel Rule. Use
the schema appropriate to your booking entity and the recipient type.

### Originator identities

Applies to every Stablecoin Payout subject to Travel Rule: Circle LLC at the
\$3,000 threshold and `CIRCLE_SG` and `CIRCLE_FR` for all amounts. The originator
identity travels in the `source.identities[]` array on `POST /v1/payouts`. It
identifies the sender of the funds, which is your business and, where
applicable, the customer that originated the transfer.

| Field                    | Type   | Required                         | Description                           |
| ------------------------ | ------ | -------------------------------- | ------------------------------------- |
| `type`                   | string | Yes                              | `individual` or `business`.           |
| `name`                   | string | Yes                              | Full legal name.                      |
| `addresses[]`            | array  | Yes                              | One or more address objects.          |
| `addresses[].line1`      | string | Yes                              | Street address.                       |
| `addresses[].line2`      | string | No                               | Additional address detail.            |
| `addresses[].city`       | string | Yes                              | City.                                 |
| `addresses[].district`   | string | Yes for United States and Canada | State or province as a 2-letter code. |
| `addresses[].postalCode` | string | Yes                              | Postal code.                          |
| `addresses[].country`    | string | Yes                              | ISO 3166-1 alpha-2 country code.      |

The following example shows a single business originator identity:

```json theme={null}
{
  "source": {
    "type": "wallet",
    "id": "12345",
    "identities": [
      {
        "type": "business",
        "name": "Acme Payments, Inc.",
        "addresses": [
          {
            "line1": "1 Market Street",
            "line2": "Suite 400",
            "city": "San Francisco",
            "district": "CA",
            "postalCode": "94105",
            "country": "US"
          }
        ]
      }
    ]
  }
}
```

### Beneficiary identity (CIRCLE\_SG and CIRCLE\_FR)

Applies to Address Book recipients used by `CIRCLE_SG`-booked and
`CIRCLE_FR`-booked payouts. The beneficiary identity travels in the `identity`
object on `POST /v1/addressBook/recipients`. It identifies the recipient. The
schema captures legal name and, for `CIRCLE_FR` recipients, an optional legal
entity identifier. Addresses are not part of this object.

| Field          | Type   | Required                    | Description                                                                                                                                                                                                   |
| -------------- | ------ | --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `type`         | string | Yes                         | `individual` or `business`.                                                                                                                                                                                   |
| `firstName`    | string | Yes when `type: individual` | Beneficiary first name.                                                                                                                                                                                       |
| `lastName`     | string | Yes when `type: individual` | Beneficiary last name.                                                                                                                                                                                        |
| `businessName` | string | Yes when `type: business`   | Beneficiary legal business name.                                                                                                                                                                              |
| `lei`          | string | No. `CIRCLE_FR` only.       | Legal entity identifier (LEI): a 20-character alphanumeric code (ISO 17442) that identifies a legal entity. Optional regardless of beneficiary type. Circle validates the format. Omitted for other entities. |

Individual beneficiary:

```json theme={null}
{
  "identity": {
    "type": "individual",
    "firstName": "Satoshi",
    "lastName": "Nakamoto"
  }
}
```

Business beneficiary:

```json theme={null}
{
  "identity": {
    "type": "business",
    "businessName": "Globex Holdings Pte. Ltd."
  }
}
```

Business beneficiary with a legal entity identifier (`CIRCLE_FR`):

```json theme={null}
{
  "identity": {
    "type": "business",
    "businessName": "Example Corp",
    "lei": "529900T8BM49AURSDO55"
  }
}
```

Circle captures the beneficiary identity at the recipient level so every payout
reuses the same verified data. After creation, you cannot modify `identity` with
`PATCH`. Attempts return error code `2036`.

### Ownership (CIRCLE\_SG and CIRCLE\_FR)

Applies to Address Book recipients used by `CIRCLE_SG` and `CIRCLE_FR`. The
ownership data travels in the `ownership` object on
`POST /v1/addressBook/recipients` and declares whether the recipient is your own
wallet or a third party's wallet, and whether that wallet is hosted by a VASP or
self-hosted.

| Field            | Type   | Required                                         | Description                                                                               |
| ---------------- | ------ | ------------------------------------------------ | ----------------------------------------------------------------------------------------- |
| `type`           | string | Yes                                              | `first_party` or `third_party`.                                                           |
| `custody.type`   | string | Yes                                              | `hosted` or `self_hosted`.                                                                |
| `custody.vaspId` | string | Yes when `custody.type: hosted`. Omit otherwise. | The VASP that holds the recipient wallet. Obtain values from `GET /v1/addressBook/vasps`. |

Third-party hosted-wallet recipient:

```json theme={null}
{
  "ownership": {
    "type": "third_party",
    "custody": {
      "type": "hosted",
      "vaspId": "f1c5e96a-2c0e-4f9c-bf63-9a8a2d3c1c12"
    }
  }
}
```

The API schema accepts `custody.type: self_hosted` for `CIRCLE_SG` and
`CIRCLE_FR`, but the risk layer denies these recipients today. Build against
hosted wallets until self-hosted support ships. After creation, you cannot
modify `ownership` with `PATCH`. Attempts return error code `2037`.

## Virtual asset service provider lookup

`GET /v1/addressBook/vasps` returns the active set of virtual asset service
providers (`VASPs`) available for your jurisdiction. The endpoint is available
to `CIRCLE_SG` and `CIRCLE_FR` customers. Use the returned `id` as
`ownership.custody.vaspId` when you register a hosted-wallet recipient.

```bash theme={null}
curl -X GET https://api-sandbox.circle.com/v1/addressBook/vasps \
  -H "Authorization: Bearer $API_KEY"
```

Sample response:

```json theme={null}
{
  "data": [
    {
      "id": "8f9a0c2e-1d3b-4a5f-9c7b-2e3d4f5a6b7c",
      "name": "Anchorage Digital"
    },
    {
      "id": "1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
      "name": "Coinhako"
    },
    {
      "id": "2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e",
      "name": "Coinbase"
    },
    {
      "id": "9b8a7c6d-5e4f-3a2b-1c0d-9e8f7a6b5c4d",
      "name": "Paxos"
    },
    {
      "id": "3c4d5e6f-7a8b-9c0d-1e2f-3a4b5c6d7e8f",
      "name": "Circle"
    },
    {
      "id": "4d5e6f7a-8b9c-0d1e-2f3a-4b5c6d7e8f90",
      "name": "Circle Singapore"
    },
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "name": "Off Network VASP"
    }
  ]
}
```

The list is dynamic. Query it at runtime rather than hardcoding IDs.

## Payment reason codes

`purposeOfTransfer` on `POST /v1/payouts` carries a payment reason code that
describes why the funds are moving. The field is required for `CIRCLE_SG`-booked
and `CIRCLE_FR`-booked payouts and is not part of Travel Rule data collection
for Circle LLC. Values align with the Cross-Border Payments Network (CPN)
payment reason codes, with one addition (`PMT000`) that is unique to Stablecoin
Payouts and is intended for cases that do not match another code. `PMT006` is
not valid for Stablecoin Payouts.

| Reason code | Description                                                                                            |
| ----------- | ------------------------------------------------------------------------------------------------------ |
| `PMT000`    | Others                                                                                                 |
| `PMT001`    | Invoice payment                                                                                        |
| `PMT002`    | Payment for services                                                                                   |
| `PMT003`    | Payment for software                                                                                   |
| `PMT004`    | Payment for imported goods                                                                             |
| `PMT005`    | Travel services                                                                                        |
| `PMT007`    | Repayment of loans                                                                                     |
| `PMT008`    | Payroll                                                                                                |
| `PMT009`    | Payment of property rental                                                                             |
| `PMT010`    | Information service charges                                                                            |
| `PMT011`    | Advertising and public relations related expenses                                                      |
| `PMT012`    | Royalty fees, trademark fees, patent fees, and copyright fees                                          |
| `PMT013`    | Fees for brokers, front end fee, commitment fee, guarantee fee, and custodian fee                      |
| `PMT014`    | Fees for advisors, technical assistance, and academic knowledge including remuneration for specialists |
| `PMT015`    | Representative office expenses                                                                         |
| `PMT016`    | Tax payment                                                                                            |
| `PMT017`    | Transportation fees for goods                                                                          |
| `PMT018`    | Construction costs/expenses                                                                            |
| `PMT019`    | Insurance premium                                                                                      |
| `PMT020`    | General goods trades (offline)                                                                         |
| `PMT021`    | Insurance claims payment                                                                               |
| `PMT022`    | Remittance payments to friends or family                                                               |
| `PMT023`    | Education-related student expenses                                                                     |
| `PMT024`    | Medical treatment                                                                                      |
| `PMT025`    | Donations                                                                                              |
| `PMT026`    | Mutual fund investment                                                                                 |
| `PMT027`    | Currency exchange                                                                                      |
| `PMT028`    | Advance payments for goods                                                                             |
| `PMT029`    | Merchant settlement                                                                                    |
| `PMT030`    | Repatriation fund settlement                                                                           |

## Supported blockchains

Travel Rule currently applies to Stablecoin Payouts on the following
blockchains:

* Algorand (`ALGO`)
* Aptos (`APTOS`)
* Arbitrum (`ARB`)
* Arc (`ARC`)
* Avalanche (`AVAX`)
* Base (`BASE`)
* Celo (`CELO`)
* Ethereum (`ETH`)
* NEAR (`NEAR`)
* Optimism (`OP`)
* Polygon PoS (`POLY`)
* XRP Ledger (`XRP`)
* Solana (`SOL`)
* Stellar (`XLM`)

Circle manages Travel Rule applicability per blockchain, and this set can
evolve. For the per-product blockchain support matrix, see
[Supported Chains and Currencies](/circle-mint/references/supported-chains-and-currencies).

## Failure modes

A Travel Rule problem surfaces in one of two places: at submission time as a
synchronous validation error, or after submission as an asynchronous risk
decision.

### Synchronous validation errors

Returned at `POST` time with an HTTP 4xx response. Fix the request and retry
with a fresh `idempotencyKey`. Address Book validation spans the `2024`-`2037`
range; `2036` and `2037` are called out separately because they cover `PATCH`
attempts on fields that are immutable after creation.

| Error code    | Trigger                                                                                                                                    |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| `5020`        | Missing or invalid `purposeOfTransfer` on a `CIRCLE_SG`-booked payout.                                                                     |
| `2024`-`2035` | Address Book recipient validation: missing `identity`, missing `ownership`, missing or invalid `custody.vaspId`, and related shape errors. |
| `2036`        | Attempt to `PATCH` `identity` on an existing Address Book recipient.                                                                       |
| `2037`        | Attempt to `PATCH` `ownership` on an existing Address Book recipient.                                                                      |

### Asynchronous risk evaluation

The payout accepts at submission with `HTTP 201`, then the resource transitions
to `failed`. The payload carries the risk decision:

```json theme={null}
{
  "data": {
    "id": "b36cbf12-6ed1-47ed-9eb9-5874f8991ca8",
    "status": "failed",
    "errorCode": "transaction_denied",
    "riskEvaluation": {
      "decision": "denied",
      "reason": "3220"
    }
  }
}
```

`reason: 3220` indicates a Travel Rule violation. Review your originator
identities, beneficiary identity (`CIRCLE_SG` and `CIRCLE_FR`), `vaspId`, and
`purposeOfTransfer` against this reference, then re-submit with a new
`idempotencyKey`.

## Receiving Travel Rule data

Regulated financial institutions can request originator identities on received
transfers by adding `returnIdentities=true` to `GET /v1/payouts/{id}` and
`GET /v1/businessAccount/transfers/{id}`:

```bash theme={null}
curl -X GET "https://api-sandbox.circle.com/v1/payouts/{id}?returnIdentities=true" \
  -H "Authorization: Bearer $API_KEY"
```

The response carries a maximum of 5 originator identity items per call.
Originator data associates with the transfer once it reaches `complete`.
Non-bank financial institutions do not receive originator identity data on
