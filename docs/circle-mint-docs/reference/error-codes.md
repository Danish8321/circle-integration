# Error codes

> Reference for synchronous and asynchronous error codes returned by the Circle Mint API, organized by product, with recommended treatment for each.

Verified live against https://developers.circle.com/circle-mint/references/error-codes on 2026-07-07 — content below unchanged.

Circle Mint surfaces errors in two shapes: synchronous responses to API calls,
and asynchronous status transitions on resources. The tables below catalog every
code, organized by product. When an endpoint returns a generic `code: -1` with a
`message` of `Something went wrong`, treat it as the transient fallback and
retry with backoff.

## Error response shape

Every synchronous error returns an HTTP status code plus a JSON body with a
numeric `code` and a human-readable `message`. Validation errors include an
extended `errors[]` array with one entry per offending field.

```json theme={null}
{
  "code": 2,
  "message": "Invalid entity."
}
```

```json theme={null}
{
  "code": 2,
  "message": "Invalid entity.",
  "errors": [
    {
      "error": "min_value",
      "message": "Must be at least 1.",
      "location": "amount",
      "invalidValue": "0",
      "constraints": { "min": 1 }
    }
  ]
}
```

Each entry in `errors[]` carries an `error` name that you can branch on
programmatically:

| Name                    | Meaning                                                     |
| ----------------------- | ----------------------------------------------------------- |
| `required`              | Field is missing.                                           |
| `min_value`             | Value is below the allowed minimum.                         |
| `max_value`             | Value exceeds the allowed maximum.                          |
| `length_outside_bounds` | String length is outside the allowed range.                 |
| `pattern_mismatch`      | Value didn't match the required regular expression pattern. |
| `date_not_in_past`      | Date must be in the past.                                   |
| `date_not_in_future`    | Date must be in the future.                                 |
| `number_format`         | Value isn't a valid number for the field.                   |
| `value_must_be_true`    | The boolean field must be `true`.                           |
| `value_must_be_false`   | The boolean field must be `false`.                          |
| `not_required`          | Field was provided but is disallowed.                       |
| `invalid_value`         | Value isn't in the allowed set for this field.              |

## Synchronous and asynchronous errors

Circle Mint surfaces errors in two distinct shapes depending on when the problem
is detected.

| Kind         | Where it surfaces                                                                                                                                                                            |
| ------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Synchronous  | Returned inline in the HTTP response with a status code (`400`, `401`, `403`, `404`, `409`, `429`, or `500`) and a JSON body containing `code` and `message`.                                |
| Asynchronous | Surfaced on a resource after it reaches a terminal `failed` (or denied) status. The resource carries a string `errorCode` and, for risk-driven denials, an optional `riskEvaluation` object. |

## Recommended treatment

Use these categories to decide how your integration should respond to a given
error.

| Category                  | Signal                                                                                    | Action                                                                                        |
| ------------------------- | ----------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Malformed request         | 4xx response with a validation `errors[]` array.                                          | Fix the request and retry with the same `idempotencyKey`.                                     |
| State conflict            | 409 response, or a `code` indicating an existing or conflicting resource.                 | Re-fetch the resource state and decide whether to retry, update, or skip.                     |
| Insufficient funds        | `insufficient_funds` on a payout or transfer, or `INSUFFICIENT_BALANCE` on a credit draw. | Top up the source wallet (or wait for a credit repayment) and retry.                          |
| Compliance or risk denial | Asynchronous `errorCode` paired with `riskEvaluation.decision = denied`.                  | Review the originator and beneficiary information, then resubmit with a new `idempotencyKey`. |
| Transient or network      | 5xx response, timeout, or generic `code: -1`.                                             | Retry with exponential backoff. If the error persists, contact Circle.                        |

## Risk evaluation reason codes

When Circle's risk engine acts on a transaction, the affected resource (a
payment, payout, or transfer) carries a `riskEvaluation` object alongside its
`errorCode`. This object explains why the risk service reached its decision:

* `decision`: the risk outcome, one of `approved`, `denied`, or `review`.
* `reason`: a numeric reason code, returned as a string such as `"3000"`, that
  identifies the specific reason for the decision.

Every reason code belongs to one of the following categories. The category tells
you the source and nature of the block, and the code range narrows it to a
specific reason.

| Category                             | Description                                                                                                                           | Code range  |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------- | ----------- |
| Circle blocked                       | The fiat account or payment contains criteria that Circle can't support, such as a prohibited country.                                | `3000-3099` |
| Processor or issuing bank            | Circle's partner processor or the issuing bank can't accept the fiat account or payment, such as an unsupported issuer country.       | `3100-3199` |
| Regulatory compliance intervention   | Circle Risk Service intervened to meet legal and regulatory compliance requirements, such as KYC verification limits.                 | `3200-3299` |
| Fraud risk intervention              | Circle Risk Service acted on the transaction because of fraud management issues, such as excessive chargeback rates.                  | `3300-3499` |
| Customer configuration (unsupported) | Circle Risk Service acted on the transaction because of your configuration or request, such as a blocked issuer country or card type. | `3500-3599` |
| Customer configuration (fraud)       | Circle Risk Service acted on the transaction because of your configuration or request, such as adding a user to a watch list.         | `3600-3699` |

The following table lists the individual reason codes in each category.

| Reason code | Description                                                                                                                                                     |
| ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `3000`      | Default.                                                                                                                                                        |
| `3001`      | Prohibited issuer (bank) country.                                                                                                                               |
| `3002`      | Prohibited billing address country.                                                                                                                             |
| `3020`      | Fiat account denied. Check the fiat account for the reason.                                                                                                     |
| `3022`      | Fiat account (card) evaluation timeout. Retry the request.                                                                                                      |
| `3023`      | Fiat account (bank account) evaluation timeout. Retry the request.                                                                                              |
| `3026`      | Fiat account is in an unverified state.                                                                                                                         |
| `3027`      | Fiat account is in a suspended state.                                                                                                                           |
| `3030`      | Unsupported bank account routing number (RTN).                                                                                                                  |
| `3040`      | Unsupported activity type.                                                                                                                                      |
| `3050`      | Customer suspended from payment processing.                                                                                                                     |
| `3070`      | Transaction exceeds the risk limits.                                                                                                                            |
| `3071`      | Daily aggregate limit exceeded (email).                                                                                                                         |
| `3072`      | Daily aggregate limit exceeded (fiat).                                                                                                                          |
| `3075`      | Weekly aggregate limit exceeded (email).                                                                                                                        |
| `3076`      | Weekly aggregate limit exceeded (fiat).                                                                                                                         |
| `3100`      | Unsupported return code response from the processor or issuing bank (default).                                                                                  |
| `3101`      | Invalid return code response from the issuing bank, for example an invalid card.                                                                                |
| `3102`      | Fraudulent return code response from the issuing bank, for example a pickup card.                                                                               |
| `3103`      | Blocked entity return code response from the processor, for example a blocked card.                                                                             |
| `3104`      | Account associated with an invalid ACH RTN.                                                                                                                     |
| `3105`      | Expired card.                                                                                                                                                   |
| `3150`      | Administrative return from the ODFI or RDFI.                                                                                                                    |
| `3151`      | Return indicating an ineligible account from the customer or RDFI.                                                                                              |
| `3152`      | Unsupported transaction type return from the customer or RDFI.                                                                                                  |
| `3200-3202` | Unsupported criteria.                                                                                                                                           |
| `3210`      | Withdrawal limit exceeded (7-day default payout limit).                                                                                                         |
| `3211`      | Withdrawal limit exceeded (7-day custom payout limit).                                                                                                          |
| `3220`      | Compliance limit exceeded. On Stablecoin Payouts this is a Travel Rule violation; see [Travel Rule Compliance](/circle-mint/references/travel-rule-compliance). |
| `3300-3309` | Transaction declined by Circle Risk Service. Contact [risk-investigations@circle.com](mailto:risk-investigations@circle.com) for more information.              |
| `3310`      | The fiat account is directly associated with fraudulent activity.                                                                                               |
| `3311`      | The email address is directly associated with fraudulent activity.                                                                                              |
| `3320`      | The fiat account is associated with a network fraud notification.                                                                                               |
| `3321`      | The email account is associated with a network fraud notification.                                                                                              |
| `3330`      | The fiat account is flagged by the Risk team.                                                                                                                   |
| `3331`      | The email account is flagged by the Risk team.                                                                                                                  |
| `3340`      | The fiat account is linked to previous fraudulent activity.                                                                                                     |
| `3341`      | The email address is linked to previous fraudulent activity.                                                                                                    |
| `3350`      | 3DS authentication is required for this transaction.                                                                                                            |
| `3500`      | Default.                                                                                                                                                        |
| `3501`      | Blocked issuer (bank) country.                                                                                                                                  |
| `3502`      | Blocked billing address country.                                                                                                                                |
| `3520`      | Blocked card type, for example credit.                                                                                                                          |
| `3530-3539` | Chargeback history on the Circle platform.                                                                                                                      |
| `3540-3549` | Chargeback history on the customer platform.                                                                                                                    |
| `3550`      | Blocked fiat (card).                                                                                                                                            |
| `3551`      | Blocked email address.                                                                                                                                          |
| `3552`      | Blocked phone number.                                                                                                                                           |
| `3600-3699` | Blocked by a fraud watch list.                                                                                                                                  |

## Common errors

These codes can appear on any endpoint.

| Code | Meaning                         | HTTP | Treatment      |
| ---- | ------------------------------- | ---- | -------------- |
| `-1` | Something went wrong.           | 500  | Transient      |
| `1`  | Malformed authorization header. | 401  | Malformed      |
| `2`  | Invalid entity.                 | 400  | Malformed      |
| `3`  | Forbidden.                      | 403  | State conflict |

## Core API errors

These codes cover the core money-movement endpoints: payments, payouts,
transfers, and blockchain address management.

| Code   | Meaning                                                                                       | Treatment          |
| ------ | --------------------------------------------------------------------------------------------- | ------------------ |
| `1077` | Payment amount must be greater than zero.                                                     | Malformed          |
| `1078` | Payment currency not supported.                                                               | Malformed          |
| `1083` | Idempotency key already bound to another request—retry with a different value.                | State conflict     |
| `1084` | This item cannot be canceled.                                                                 | State conflict     |
| `1085` | This item cannot be refunded.                                                                 | State conflict     |
| `1086` | This payment was already canceled.                                                            | State conflict     |
| `1087` | Total amount to be refunded exceeds the original payment amount.                              | Malformed          |
| `1088` | Invalid source account specified in a payout or transfer request.                             | Malformed          |
| `1089` | The source account could not be found.                                                        | Malformed          |
| `1093` | Source account has insufficient funds for the payout or transfer amount.                      | Insufficient funds |
| `1096` | Encryption key ID could not be found; an encryption key ID is required for encrypted data.    | Malformed          |
| `1097` | Cannot cancel or refund a failed payment.                                                     | State conflict     |
| `1100` | Invalid country format.                                                                       | Malformed          |
| `1101` | Invalid country format—provide a valid ISO 3166-1 alpha-2 country code.                       | Malformed          |
| `1106` | Invalid district format—must be a 2-character value.                                          | Malformed          |
| `1107` | Payout limit exceeded.                                                                        | State conflict     |
| `1108` | Country not supported for customer.                                                           | Malformed          |
| `1112` | Country or district error on a request payload.                                               | Malformed          |
| `2003` | The recipient blockchain address is already associated with the account.                      | State conflict     |
| `2004` | The blockchain address is not a verified withdrawal address.                                  | State conflict     |
| `2005` | The blockchain address belongs to an unsupported blockchain.                                  | Malformed          |
| `2006` | The wallet type specified when creating an end-user wallet is not supported.                  | Malformed          |
| `2007` | A transfer from the provided source to the provided destination is not supported.             | Malformed          |
| `2009` | Unsupported transfer configuration.                                                           | Malformed          |
| `2020` | Unsupported transfer request.                                                                 | Malformed          |
| `5001` | Payout doesn't exist—verify the payout ID.                                                    | Malformed          |
| `5002` | Payout amount must be greater than zero.                                                      | Malformed          |
| `5003` | Inactive destination address. Addresses may require a 24-hour wait after creation before use. | State conflict     |
| `5004` | The destination address for this payout could not be found.                                   | Malformed          |
| `5005` | The source wallet for this payout could not be found.                                         | Malformed          |
| `5006` | The source wallet has insufficient funds for this payout.                                     | Insufficient funds |
| `5007` | Currency not supported for this operation.                                                    | Malformed          |
| `5011` | Invalid destination address.                                                                  | Malformed          |
| `5012` | Cannot search for both crypto and fiat payouts at the same time.                              | Malformed          |
| `5013` | Source wallet ID must be a number for payouts search.                                         | Malformed          |
| `5014` | The blockchain address is not valid for the corresponding blockchain.                         | Malformed          |
| `5015` | The destination blockchain doesn't match the currency used.                                   | Malformed          |
| `5017` | Destination address or blockchain configuration error on a payout request.                    | Malformed          |

## Stablecoin Payins errors

Most Stablecoin Payins asynchronous failures surface via the `payments` webhook
and the `paymentIntent` lifecycle rather than through discrete error codes. The
synchronous codes below cover checkout-session validation.

| Code   | Meaning                                                                  | Treatment      |
| ------ | ------------------------------------------------------------------------ | -------------- |
| `1143` | Checkout session not found—the supplied session ID doesn't exist.        | Malformed      |
| `1144` | Checkout session is already in a completed state and cannot be extended. | State conflict |

For asynchronous failure events on payment intents and payments, see
[Webhook notifications](/circle-mint/references/webhook-notifications).

## Stablecoin Payouts errors

Stablecoin Payouts errors have two sources. Synchronous validation covers Travel
Rule fields and Address Book entity validation. After submission, the payout
entity itself can transition to `failed` with an `errorCode` that describes why.

### Synchronous validation

For full Travel Rule context, see
[Travel rule compliance](/circle-mint/references/travel-rule-compliance).

| Code   | Meaning                                                                                | Treatment |
| ------ | -------------------------------------------------------------------------------------- | --------- |
| `5020` | `purposeOfTransfer` field is missing or invalid on a `CIRCLE_SG`-booked crypto payout. | Malformed |
| `2024` | Address Book identity is missing for this entity.                                      | Malformed |
| `2025` | Address Book ownership is missing for this entity.                                     | Malformed |
| `2026` | Address Book VASP ID is missing for this entity.                                       | Malformed |
| `2027` | Address Book ownership type is invalid for this entity.                                | Malformed |
| `2028` | Address Book custody type is invalid for this entity.                                  | Malformed |
| `2029` | Address Book custody is missing for this entity.                                       | Malformed |
| `2030` | Address Book VASP ID is invalid for this entity.                                       | Malformed |
| `2031` | Address Book identity type is invalid—must be `individual` or `business`.              | Malformed |
| `2032` | Address Book identity first name is missing for an individual identity.                | Malformed |
| `2033` | Address Book identity last name is missing for an individual identity.                 | Malformed |
| `2034` | Address Book identity business name is missing for a business identity.                | Malformed |
| `2035` | Address Book VASP ID is not allowed—VASP ID must be omitted for self-hosted custody.   | Malformed |
| `2036` | Identity is not allowed in Address Book `PATCH` requests.                              | Malformed |
| `2037` | Ownership is not allowed in Address Book `PATCH` requests.                             | Malformed |

### Asynchronous entity errors

| `errorCode`                   | Meaning                                                                       |
| ----------------------------- | ----------------------------------------------------------------------------- |
| `insufficient_funds`          | Source wallet doesn't have enough USDC for the payout.                        |
| `transaction_denied`          | Payout was denied by Circle Risk Service—see `riskEvaluation` for the reason. |
| `transaction_failed`          | Payout failed due to an unknown reason.                                       |
| `transaction_returned`        | Payout was returned by the receiving network.                                 |
| `fiat_account_limit_exceeded` | The fiat account limit was exceeded.                                          |

<Note>
  When `riskEvaluation.reason` is `3220`, the denial is a Travel Rule violation.
  Review your originator identities, beneficiary identity, VASP ID, and
  `purposeOfTransfer`, then resubmit with a new `idempotencyKey`. See [Travel
  Rule Compliance](/circle-mint/references/travel-rule-compliance). For the full
  list of `riskEvaluation.reason` values, see [Risk evaluation reason
  codes](#risk-evaluation-reason-codes).
</Note>

## Credit errors

The Credit API's draw, fee, and repayment endpoints surface validation failures
via a top-level `validationErrors` array on the credit line. When
`validationErrors` is non-empty, draw and repayment endpoints return HTTP 400
until the listed conditions clear.

| Value                  | Meaning                                                                                 | Treatment      |
| ---------------------- | --------------------------------------------------------------------------------------- | -------------- |
| `INSUFFICIENT_BALANCE` | The credit line's Circle Mint wallet balance is below `minBalance`, blocking new draws. | State conflict |
| `PENDING_FEES`         | An unpaid fee is blocking new draws.                                                    | State conflict |
| `OVERDUE_TRANSFERS`    | At least one disbursed transfer is past its due date.                                   | State conflict |

Calling `POST /v1/credit/cryptoRepayment` against a Settlement Advance credit
line returns HTTP 400—crypto repayment is only supported on Line of Credit
lines. See the [Credit API concept](/circle-mint/concepts/credit-api) for
product differences.

## Institutional Distribution errors

These responses surface when you create or manage external entities through the
Institutional API.

| HTTP  | Meaning                                                                                                                                                     | Treatment                                                 |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| `400` | Missing or invalid fields on external entity creation.                                                                                                      | Malformed                                                 |
| `401` | The API key doesn't have the Institutional Distribution permission.                                                                                         | State conflict: contact Circle to enable the entitlement. |
| `409` | An external entity with the supplied `businessUniqueIdentifier` and `identifierIssuingCountryCode` already exists. Re-fetch via `GET /v1/externalEntities`. | State conflict                                            |

## Custody balance errors

These codes apply to daily user balance reporting for custody accounts.

| Code     | Meaning                                                                                                      | Treatment      |
| -------- | ------------------------------------------------------------------------------------------------------------ | -------------- |
| `500000` | Unsupported currency for reporting daily user balance—must be USDC or EURC.                                  | Malformed      |
| `500001` | Custody balance `asOfDate` is too far from today's date.                                                     | Malformed      |
| `500002` | Custody balance cannot be less than zero.                                                                    | Malformed      |
| `500003` | Local custody balance can't be greater than total custody balance.                                           | Malformed      |
| `500004` | Idempotency key can't be reused across different requests.                                                   | State conflict |
| `500005` | A custody balance report already exists for the provided date and currency. Contact customer care for edits. | State conflict |

## Transfer entity errors

These `errorCode` values appear on a transfer resource after it transitions to
`failed`.

| `errorCode`          | Meaning                                                  | Treatment                                                       |
| -------------------- | -------------------------------------------------------- | --------------------------------------------------------------- |
| `transfer_failed`    | Transfer could not be completed.                         | Transient: investigate `riskEvaluation` and network conditions. |
| `transfer_denied`    | Transfer was denied by Circle Risk Service.              | Compliance: review and resubmit with a new `idempotencyKey`.    |
| `blockchain_error`   | Onchain failure prevented the transfer from settling.    | Transient: retry once network is healthy.                       |
| `insufficient_funds` | Source wallet doesn't have enough USDC for the transfer. | Insufficient funds                                              |
