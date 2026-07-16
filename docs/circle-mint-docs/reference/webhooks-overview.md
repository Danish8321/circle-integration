# Webhooks

> Learn how Circle uses webhooks to notify your application when events occur, including the event model, delivery behavior, and idempotency.

Verified live 2026-07-07 at https://developers.circle.com/api-reference/webhooks
(moved out of `circle-mint/*`) — content below unchanged.

Circle uses webhooks to notify your application when events occur. Because many
operations across Circle products complete asynchronously, your application
doesn't block on the result of an API call. Instead, Circle sends an HTTP `POST`
request to an endpoint you configure when the state of a resource changes, so
your application can react to the event.

## Notification API versions

Circle offers two notification systems. Which one you integrate with depends on
the product you're using.

| Version | Delivery                              | Products                                                          |
| ------- | ------------------------------------- | ----------------------------------------------------------------- |
| **v2**  | Direct HTTPS POST from Circle         | Circle Wallets, Circle Contracts, CPN payments, Gateway, StableFX |
| **v1**  | Amazon SNS publishes to your endpoint | Circle Mint, Digital Asset Accounts, CPN Managed Payments         |

The two versions differ in delivery transport, subscription registration,
envelope shape, and signature verification. See
[Set up a webhook endpoint](/api-reference/webhook-endpoints) for the setup flow
that matches your product.

## Event model

Each webhook notification is an HTTP `POST` request to a subscriber endpoint you
configure. The envelope shape depends on the notification API version.

<Tabs>
  <Tab title="v2 notifications">
    Used by Circle Wallets, Circle Contracts, CPN payments, Gateway, and
    StableFX. Each notification includes:

    | Field              | Type            | Description                                                                                                                 |
    | ------------------ | --------------- | --------------------------------------------------------------------------------------------------------------------------- |
    | `subscriptionId`   | string (UUIDv4) | Identifies the subscription that produced the notification. Use it to route events to the correct handler.                  |
    | `notificationId`   | string (UUIDv4) | Uniquely identifies the notification. The same ID is reused if Circle retries delivery, so use it to deduplicate.           |
    | `notificationType` | string          | The event type (for example, `transactions.inbound`, `cpn.payment.completed`).                                              |
    | `notification`     | object          | The resource that changed, including its current state. The shape matches the corresponding API endpoint's response object. |
    | `timestamp`        | string          | ISO 8601 timestamp of the event.                                                                                            |
    | `version`          | number          | Schema version. Always `2`.                                                                                                 |

    You subscribe to the events your application cares about. When a
    corresponding state change occurs in Circle's systems, Circle sends a
    notification to your endpoint.
  </Tab>

  <Tab title="v1 notifications">
    Used by Circle Mint, Digital Asset Accounts, and CPN Managed Payments. v1
    uses Amazon Simple Notification Service (SNS) as the delivery layer, so
    each notification arrives as an SNS message wrapping the Circle event
    payload.

    The outer SNS envelope includes:

    | Field            | Type   | Description                                                                                                                          |
    | ---------------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------ |
    | `Type`           | string | The SNS message type. `Notification` for events, `SubscriptionConfirmation` for the one-time handshake described below.              |
    | `MessageId`      | string | Unique identifier for this delivery. Reused if SNS retries, so use it to deduplicate.                                                |
    | `TopicArn`       | string | The SNS topic the subscription is bound to.                                                                                          |
    | `Message`        | string | The Circle event payload, encoded as a JSON string. Parse it to access the inner envelope described next.                            |
    | `Signature`      | string | Base64-encoded signature of the canonical message string.                                                                            |
    | `SigningCertURL` | string | URL of the public certificate used to verify `Signature`. See [Verify webhook signatures](/api-reference/verify-webhook-signatures). |

    The inner Circle envelope, parsed from `Message`, includes:

    | Field              | Type            | Description                                                                                                                  |
    | ------------------ | --------------- | ---------------------------------------------------------------------------------------------------------------------------- |
    | `clientId`         | string (UUIDv4) | Identifies the Circle account that owns the resource.                                                                        |
    | `notificationType` | string          | The event topic (for example, `deposits`, `transfers`). The topic determines which resource key is present on the envelope.  |
    | `version`          | number          | Schema version for the notification payload.                                                                                 |
    | `customAttributes` | object          | Echoes selected envelope attributes such as `clientId`.                                                                      |
    | `<resourceKey>`    | object          | The topic-specific resource (for example, `deposit`, `transfer`, `payout`) carrying the current state of the changed object. |

    When you register a v1 subscription, your endpoint first receives a
    one-time `SubscriptionConfirmation` message containing a `SubscribeURL`.
    Visit that URL to complete the handshake before events begin flowing. See
    [Set up a webhook endpoint](/api-reference/webhook-endpoints#v1-notifications)
    for the full flow.
  </Tab>
</Tabs>

## Delivery and idempotency

Circle delivers webhook notifications at least once. If your endpoint does not
respond with a success status, or if the request fails, Circle retries delivery.
The same notification can be sent more than once.

Your endpoint must be idempotent: processing the same notification more than
once must not cause incorrect state in your application. Deduplicate on the
**Notification ID** (v2) or **MessageId** (v1) before applying side effects.

<Tip>
  To inspect delivery attempts, view payloads, or resend a notification, view
  Webhook Logs in the [Circle Console](https://console.circle.com) (Wallets,
  Contracts) or [CPN Console](https://cpn.circle.com) (CPN).
</Tip>
