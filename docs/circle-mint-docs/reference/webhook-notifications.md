# Webhook notifications

> Reference for Circle Mint webhook event topics with example payloads.

Verified live against https://developers.circle.com/circle-mint/references/webhook-notifications on 2026-07-07 — content below unchanged.

This page lists every Circle Mint webhook event topic and shows an example
payload for each. Mint webhooks are delivered as Amazon SNS messages on the
[v1 notification system](/api-reference/webhooks#notification-api-versions). To
register a webhook endpoint, see
[Set up a webhook endpoint](/api-reference/webhook-endpoints#v1-notifications).
If you do not have a Circle Mint account, start with the
[Getting Started](/circle-mint/quickstarts/getting-started) quickstart.

## Event types

The table below lists every Circle Mint webhook topic and the resource each
notification carries. The subsections that follow describe each topic, document
its status values when applicable, and show a single example payload.

| Topic                              | Resource                | What it reports                                                                               |
| ---------------------------------- | ----------------------- | --------------------------------------------------------------------------------------------- |
| `wire`                             | Wire bank account       | Lifecycle of a linked wire bank account: `pending`, `complete`, or `failed`.                  |
| `deposits`                         | Deposit                 | Settlement of a fiat deposit (mint) to your Circle Mint balance.                              |
| `payouts`                          | Payout                  | Lifecycle of a fiat redemption (burn) or stablecoin payout.                                   |
| `transfers`                        | Transfer                | Onchain transfer status transitions in either direction.                                      |
| `paymentIntents`                   | Payment intent          | Stablecoin Payins intent lifecycle, including deposit-address assignment and timeline events. |
| `payments`                         | Payment                 | Settled Stablecoin Payins payment, or a Stablecoin Payouts refund.                            |
| `addressBookRecipients`            | Address book recipient  | Recipient review outcome and Travel Rule decision for Stablecoin Payouts.                     |
| `externalEntities`                 | External entity         | Core API for Institutions compliance outcome for an onboarded entity.                         |
| `creditTransfers`                  | Credit transfer         | Status transitions for a Settlement Advance or Line of Credit draw.                           |
| `creditFees`                       | Credit fee              | Fee accruals against a credit line.                                                           |
| `creditRepayments`                 | Credit repayment        | Matched fiat repayment or completed crypto repayment against a credit line.                   |
| `approvalWorkflowTransferApproved` | Approval workflow event | A pending transfer was approved through the recipient approval workflow.                      |
| `approvalWorkflowTransferRejected` | Approval workflow event | A pending transfer was rejected through the recipient approval workflow.                      |

### `wire`

Lifecycle notifications for a linked wire bank account. Use these events to
track the status of bank-account creation requests submitted through
[`POST /v1/businessAccount/banks/wires`](/api-reference/circle-mint/account/create-business-wire-account).

| Status     | Meaning                                                              |
| ---------- | -------------------------------------------------------------------- |
| `pending`  | Circle is reviewing the bank account.                                |
| `complete` | The bank account is linked and can be used for deposits and payouts. |
| `failed`   | The bank account could not be linked.                                |

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "wire",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "wire": {
      "id": "8c33b3eb-67a4-4f3d-9f4e-2d8a4f1c2b6a",
      "status": "complete",
      "description": "WELLS FARGO BANK, NA ****0010",
      "trackingRef": "CIR2VKZ9G6",
      "fingerprint": "eb74e904-2c64-4f4d-9f54-9f1b8f7a2bb1",
      "billingDetails": {
        "name": "Satoshi Nakamoto",
        "city": "Boston",
        "country": "US",
        "line1": "100 Money Street",
        "district": "MA",
        "postalCode": "02201"
      },
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:25:11.000Z"
    }
  }
  ```
</Accordion>

### `deposits`

Fires when a fiat deposit (mint) settles to your Circle Mint balance. The
notification carries the deposit `id`, the settled `amount`, the `source` (the
linked wire bank account), and the destination wallet.

| Status     | Meaning                                                         |
| ---------- | --------------------------------------------------------------- |
| `pending`  | Circle has received the wire and is processing the mint.        |
| `complete` | USDC or EURC has been credited to the destination wallet.       |
| `failed`   | The deposit could not be completed and funds were not credited. |

When the originating wire memo included a `customerExternalRef` matching the
`EXT...` format (used by the Core API for Institutions), Circle echoes that
value on the deposit so you can reconcile the credit to the originating client.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "deposits",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "deposit": {
      "id": "df3b8e5f-9579-4c1f-9fa9-deac7f4be55c",
      "status": "complete",
      "amount": { "amount": "1000.00", "currency": "USD" },
      "fees": { "amount": "0.00", "currency": "USD" },
      "source": { "id": "8c33b3eb-67a4-4f3d-9f4e-2d8a4f1c2b6a", "type": "wire" },
      "destination": { "id": "1000038499", "type": "wallet" },
      "customerExternalRef": "EXT0000000000000000A1",
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:26:02.000Z"
    }
  }
  ```
</Accordion>

### `payouts`

Fires for fiat redemptions (burns) and stablecoin payouts. The payout resource
carries the destination (a `wire` bank account or an `address_book` recipient),
the gross `amount`, fees, and any error information.

The most important fields to consume:

* `id`: Unique payout identifier.
* `destination`: Either a `wire` destination (fiat redemption) or an
  `address_book` destination (stablecoin payout).
* `amount`: Gross amount of the payout, as a money object.
* `toAmount`: Net amount delivered to the destination. Present only on
  stablecoin payouts.
* `trackingRef`: Reference that appears on the bank statement. Present on fiat
  redemption (burn) payouts.
* `sourceWalletId`: Identifier of the wallet funding the payout.
* `fees`: Fees deducted from the source wallet, as a money object.
* `networkFees`: Onchain network fees. Present only on stablecoin payouts.
* `status`: `pending`, `complete`, or `failed`.
* `errorCode`: Populated when `status` is `failed`. See
  [Stablecoin Payouts errors](/circle-mint/references/error-codes#stablecoin-payouts-errors)
  for the catalog.
* `riskEvaluation`: Risk decision and reason, populated for compliance-driven
  denials.

| Status     | Meaning                                                            |
| ---------- | ------------------------------------------------------------------ |
| `pending`  | The payout is in progress.                                         |
| `complete` | Funds have been delivered to the destination.                      |
| `failed`   | The payout could not be completed. See `errorCode` for the reason. |

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "payouts",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "payout": {
      "id": "df3b8e5f-9579-4c1f-9fa9-deac7f4be55c",
      "sourceWalletId": "1000038499",
      "destination": {
        "id": "4d260293-d17c-4309-a8da-fa7850f95c10",
        "type": "address_book"
      },
      "amount": { "amount": "100.00", "currency": "USD" },
      "toAmount": { "amount": "100.00", "currency": "USD" },
      "fees": { "amount": "0.50", "currency": "USD" },
      "networkFees": { "amount": "0.10", "currency": "USD" },
      "trackingRef": "CIR2VKZ9G6",
      "status": "complete",
      "riskEvaluation": { "decision": "approved", "reason": "1000" },
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:26:02.000Z"
    }
  }
  ```
</Accordion>

### `transfers`

Fires on every status transition for an onchain transfer, in either direction
(Circle wallet to blockchain address, blockchain address to Circle wallet, or
wallet to wallet). You receive one notification per transition, so a transfer
that runs to completion produces multiple events.

| Status     | Meaning                                                              |
| ---------- | -------------------------------------------------------------------- |
| `pending`  | The transfer has been submitted and is awaiting onchain broadcast.   |
| `running`  | The transfer is broadcast and waiting for confirmations.             |
| `complete` | The transfer is confirmed onchain and settled.                       |
| `failed`   | The transfer could not be completed. See `errorCode` for the reason. |

When `status` is `failed`, the transfer carries an `errorCode`; see
[Transfer entity errors](/circle-mint/references/error-codes#transfer-entity-errors)
for the catalog.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "transfers",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "transfer": {
      "id": "0d46b642-3a5f-4071-a747-4053b7df2f99",
      "source": { "type": "wallet", "id": "1000038499" },
      "destination": {
        "type": "blockchain",
        "address": "0x8381470ED67C3802402dbbFa0058E8871F017A6F",
        "chain": "ARC"
      },
      "amount": { "amount": "3.14", "currency": "USD" },
      "transactionHash": "0x4cebf8f90c9243a23c77e4ae20df691469e4b933b295a73376292843968f7a63",
      "status": "complete",
      "riskEvaluation": { "decision": "approved", "reason": "1000" },
      "createDate": "2026-01-15T18:23:44.000Z"
    }
  }
  ```
</Accordion>

### `paymentIntents`

Fires on lifecycle changes for a Stablecoin Payins payment intent, including
deposit-address assignment, settlement, expiry, refund, and failure. The payload
includes the intent's full state—`paymentMethods[]` (the assigned deposit
addresses), `timeline[]` (an ordered history of `status` and `context`
transitions with timestamps), `amountPaid`, `amountRefunded`,
`settlementCurrency`, and `fees[]`.

The intent's lifecycle depends on `type`:

* `continuous` intents stay at `active` after Circle assigns the deposit address
  and never advance to `complete`. To reconcile settled transfers against a
  continuous intent, listen to the `payments` topic or call
  [`GET /v1/payments`](/api-reference/circle-mint/payments/list-payments) with
  `paymentIntentId={id}`.
* `transient` intents move through `created` → `pending` → `complete`, where the
  latest `timeline[]` entry carries a `context` of `paid`, `underpaid`, or
  `overpaid`. Terminal states are `expired`, `failed`, and `refunded`.

For background on intent modes and the payin flow, see
[How Stablecoin Payins and Payouts work](/cpn/stablecoin-payments/concepts/how-stablecoin-payments-work).

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "paymentIntents",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "paymentIntent": {
      "id": "5cb31987-66f1-4ce6-87ce-fb74dfe7c2dd",
      "type": "continuous",
      "amount": { "amount": "0.00", "currency": "USD" },
      "amountPaid": { "amount": "0.00", "currency": "USD" },
      "amountRefunded": { "amount": "0.00", "currency": "USD" },
      "settlementCurrency": "USD",
      "paymentMethods": [
        {
          "type": "blockchain",
          "chain": "ARC",
          "address": "0x8381470ED67C3802402dbbFa0058E8871F017A6F"
        }
      ],
      "fees": [],
      "timeline": [
        { "status": "active", "time": "2026-01-15T18:23:44.000Z" },
        { "status": "created", "time": "2026-01-15T18:23:40.000Z" }
      ],
      "createDate": "2026-01-15T18:23:40.000Z",
      "updateDate": "2026-01-15T18:23:44.000Z"
    }
  }
  ```
</Accordion>

### `payments`

Fires for inbound Stablecoin Payins settlements and for Stablecoin Payouts
refunds. Both flows use the same `payments` resource; the `type` field
discriminates between them: `payment` for an inbound settlement and `refund` for
an outbound refund.

| Status            | Meaning                                                                        |
| ----------------- | ------------------------------------------------------------------------------ |
| `pending`         | The payment has been observed and is awaiting confirmations.                   |
| `confirmed`       | The payment has reached the required number of onchain confirmations.          |
| `paid`            | The payment has settled to your Circle Mint balance.                           |
| `failed`          | The payment could not be settled.                                              |
| `action_required` | Manual intervention is required, such as a refund decision on an underpayment. |

<Accordion title="Example payment payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "payments",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "payment": {
      "id": "b9aef7d4-2eb4-4f4a-9b7f-71b3a4c9bb3b",
      "type": "payment",
      "status": "paid",
      "paymentIntentId": "5cb31987-66f1-4ce6-87ce-fb74dfe7c2dd",
      "amount": { "amount": "250.00", "currency": "USD" },
      "feeAmount": { "amount": "0.50", "currency": "USD" },
      "source": {
        "type": "blockchain",
        "chain": "ARC",
        "address": "0x6E1A4C16fAFC4ec1Aa1Dc6Fbe5cE9aB2B22B3F11"
      },
      "depositAddress": {
        "chain": "ARC",
        "address": "0x8381470ED67C3802402dbbFa0058E8871F017A6F"
      },
      "transactionHash": "0x4cebf8f90c9243a23c77e4ae20df691469e4b933b295a73376292843968f7a63",
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:25:11.000Z"
    }
  }
  ```
</Accordion>

<Accordion title="Example refund payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "payments",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "payment": {
      "id": "c12ab9d6-3fc7-4ec1-92d4-58c1a4d5fae2",
      "type": "refund",
      "status": "paid",
      "paymentIntentId": "5cb31987-66f1-4ce6-87ce-fb74dfe7c2dd",
      "originalPaymentId": "b9aef7d4-2eb4-4f4a-9b7f-71b3a4c9bb3b",
      "amount": { "amount": "250.00", "currency": "USD" },
      "settlementAmount": { "amount": "250.00", "currency": "USD" },
      "destination": {
        "type": "blockchain",
        "chain": "ARC",
        "address": "0x6E1A4C16fAFC4ec1Aa1Dc6Fbe5cE9aB2B22B3F11"
      },
      "transactionHash": "0x9fbe04f70cb1d5f5c12c93dcb2e21a8d6c3b8a4f93e1c01e2c3a4b5c6d7e8f90",
      "createDate": "2026-01-15T19:02:11.000Z",
      "updateDate": "2026-01-15T19:04:48.000Z"
    }
  }
  ```
</Accordion>

### `addressBookRecipients`

Fires on lifecycle transitions for a Stablecoin Payouts recipient registered
through the Address Book API. Use this topic to learn when a recipient is ready
to receive payouts and to handle Travel Rule decisions on counterparties.

| Status     | Meaning                                                     |
| ---------- | ----------------------------------------------------------- |
| `pending`  | Circle is reviewing the recipient.                          |
| `inactive` | The recipient is in the delayed-withdrawals holding period. |
| `active`   | The recipient is ready to receive payouts.                  |
| `denied`   | The recipient failed review and cannot receive payouts.     |

For Travel Rule requirements and identity schemas that influence these
decisions, see
[Travel rule compliance](/circle-mint/references/travel-rule-compliance).

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "addressBookRecipients",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "addressBookRecipient": {
      "id": "4d260293-d17c-4309-a8da-fa7850f95c10",
      "status": "active",
      "chain": "ARC",
      "address": "0x8381470ED67C3802402dbbFa0058E8871F017A6F",
      "nickname": "Vendor wallet",
      "metadata": {
        "nickname": "Vendor wallet",
        "type": "business"
      },
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:26:02.000Z"
    }
  }
  ```
</Accordion>

### `externalEntities`

Fires when Circle finishes its compliance review for an external entity
onboarded through the Institutional API. The webhook carries the final
`complianceState` decision; `walletId` is present only when the decision is
`ACCEPTED`.

| `complianceState` | Meaning                                                                          |
| ----------------- | -------------------------------------------------------------------------------- |
| `PENDING`         | The entity is under review. The accompanying `walletId` is unusable.             |
| `ACCEPTED`        | The entity passed review. The accompanying `walletId` is provisioned and usable. |
| `REJECTED`        | The entity failed review and cannot operate through Circle Mint.                 |

For background on the onboarding flow and how to use a provisioned `walletId`,
see [Institutional API](/circle-mint/concepts/institutional-api) and
[Manage institutional subaccounts](/circle-mint/quickstarts/manage-institutional-subaccounts).

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "externalEntities",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "externalEntity": {
      "id": "9a4d6e72-78aa-4b9d-bd84-2cc7c1a8c2d4",
      "complianceState": "ACCEPTED",
      "walletId": "212000",
      "businessName": "Example Trading Ltd.",
      "businessUniqueIdentifier": "TAX-001",
      "identifierIssuingCountryCode": "US",
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:30:00.000Z"
    }
  }
  ```
</Accordion>

### `creditTransfers`

Fires when a credit draw changes status. The two Credit API products follow
different lifecycles, so the meaningful status values depend on which product
the credit line is scoped to.

Settlement Advance status values:

| Status           | Meaning                                                                              |
| ---------------- | ------------------------------------------------------------------------------------ |
| `funds_reserved` | Capacity is reserved against the credit line; the reservation expires in 30 minutes. |
| `requested`      | Wire proof has been submitted and Treasury is reviewing the request.                 |
| `disbursed`      | Treasury approved the advance and funds have landed.                                 |
| `paid`           | The disbursed amount is fully repaid.                                                |
| `past_due`       | The disbursed amount has not been fully repaid by its due date.                      |
| `expired`        | The reservation timed out before progressing to `requested`.                         |
| `canceled`       | You canceled the reservation before submitting wire proof.                           |
| `rejected`       | Treasury declined the request.                                                       |

Line of Credit status values:

| Status      | Meaning                                                                              |
| ----------- | ------------------------------------------------------------------------------------ |
| `requested` | The draw was created and is being processed for disbursement.                        |
| `disbursed` | Funds have landed in your Mint wallet or at the verified Credit Express destination. |
| `paid`      | The disbursed amount is fully repaid.                                                |
| `past_due`  | The disbursed amount has not been fully repaid by its due date.                      |
| `rejected`  | Circle declined the request.                                                         |

When a transfer is configured with a Credit Express destination, the
disbursement carries an additional onchain delivery status:

| Credit Express destination status | Meaning                                     |
| --------------------------------- | ------------------------------------------- |
| `pending`                         | The onchain delivery has not started.       |
| `initiated`                       | The onchain transaction has been broadcast. |
| `complete`                        | The onchain delivery is confirmed.          |
| `failed`                          | The onchain delivery failed.                |

For background on the credit-line model and the two product lifecycles, see the
[Credit API](/circle-mint/concepts/credit-api) concept.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "creditTransfers",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "creditTransfer": {
      "id": "c3a2f1e0-7e8c-4b3d-9b9b-1f4e2a7d4c11",
      "product": "lineOfCredit",
      "status": "disbursed",
      "amount": { "amount": "50000.00", "currency": "USD" },
      "outstandingAmount": { "amount": "50000.00", "currency": "USD" },
      "destination": { "type": "wallet", "id": "1000038499" },
      "disbursedAt": "2026-01-15T18:23:44.000Z",
      "dueAt": "2026-01-22T18:23:44.000Z",
      "createDate": "2026-01-15T18:23:00.000Z",
      "updateDate": "2026-01-15T18:23:44.000Z"
    }
  }
  ```
</Accordion>

### `creditFees`

Fires when a fee accrues against a credit line. Cadence matches the credit
line's `feeCadence`: `daily` lines emit a fee notification every 24 hours and
`hourly` Line of Credit lines emit one every hour. Each notification carries the
accrued amount, the credit line it applies to, and the period it covers.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "creditFees",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "creditFee": {
      "id": "fee-1ef1f5cf-2b1b-4f12-aa6c-2c4f9b8fb3da",
      "creditLineId": "9c2c5f4d-1b6c-4a5f-9d0e-3a4b5c6d7e8f",
      "creditTransferId": "c3a2f1e0-7e8c-4b3d-9b9b-1f4e2a7d4c11",
      "type": "recurringFee",
      "amount": { "amount": "50.00", "currency": "USD" },
      "feeCadence": "daily",
      "periodStart": "2026-01-15T18:23:44.000Z",
      "periodEnd": "2026-01-16T18:23:44.000Z",
      "createDate": "2026-01-16T18:23:44.000Z"
    }
  }
  ```
</Accordion>

### `creditRepayments`

Fires when Circle matches an incoming fiat wire to a credit line or records a
completed crypto repayment. The payload identifies the credit line, the
repayment method (`fiat` or `crypto`), the matched amount, and the resulting
outstanding balance.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "creditRepayments",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "creditRepayment": {
      "id": "rep-3c8d9e0f-1a2b-4c5d-6e7f-8a9b0c1d2e3f",
      "creditLineId": "9c2c5f4d-1b6c-4a5f-9d0e-3a4b5c6d7e8f",
      "creditTransferId": "c3a2f1e0-7e8c-4b3d-9b9b-1f4e2a7d4c11",
      "type": "fiat",
      "status": "complete",
      "amount": { "amount": "50050.00", "currency": "USD" },
      "outstandingAmount": { "amount": "0.00", "currency": "USD" },
      "createDate": "2026-01-22T16:00:00.000Z",
      "updateDate": "2026-01-22T16:05:00.000Z"
    }
  }
  ```
</Accordion>

### `approvalWorkflowTransferApproved` and `approvalWorkflowTransferRejected`

Recipient approval workflow events. Some regions require a separate approval
step before a transfer can proceed; France and Singapore are two examples.
Pending transfers are routed to an approver. The decision is delivered on
`approvalWorkflowTransferApproved` when the proposal is approved or on
`approvalWorkflowTransferRejected` when it is rejected. The payload carries the
proposal's `transferStatus`, `proposalStatus`, the originating `idempotencyKey`,
and the `transferId` of the affected transfer.

<Accordion title="Example payload">
  ```json theme={null}
  {
    "clientId": "00000000-0000-0000-0000-000000000001",
    "notificationType": "approvalWorkflowTransferApproved",
    "version": 1,
    "customAttributes": { "clientId": "00000000-0000-0000-0000-000000000001" },
    "approvalWorkflow": {
      "transferId": "0d46b642-3a5f-4071-a747-4053b7df2f99",
      "transferStatus": "pending",
      "proposalStatus": "approved",
      "idempotencyKey": "ba943ff1-ca16-49b2-ba55-1057e70ca5c7",
      "createDate": "2026-01-15T18:23:44.000Z",
      "updateDate": "2026-01-15T18:28:00.000Z"
    }
  }
  ```
</Accordion>
