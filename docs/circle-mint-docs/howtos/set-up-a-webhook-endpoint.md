# How-to: Set up a webhook endpoint

> Expose a subscriber endpoint and subscribe to webhook notifications from a Circle product.

Verified live 2026-07-16 at https://developers.circle.com/api-reference/webhook-endpoints
(corrected — `/api-reference/webhooks` is a different page, the product-agnostic
webhook event-model/delivery/ordering overview, not this setup guide)
— content below unchanged, v1 section is the accurate Circle Mint flow.

To start receiving webhook notifications from a Circle product, expose a
subscriber endpoint, then register that endpoint as a subscriber to the events
you care about. The exact flow depends on which
[notification API version](/api-reference/webhooks#notification-api-versions)
the product uses.

<Tabs>
  <Tab title="v2 notifications">
    Used by [Circle Wallets](/wallets), [Circle Contracts](/contracts),
    [CPN payments](/cpn), [Gateway](/gateway), and [StableFX](/stablefx).

    <Steps>
      <Step title="Set up a subscriber endpoint">
        Expose a publicly accessible HTTPS endpoint that:

        * Is reachable from the public internet.
        * Handles both `HEAD` and `POST` requests. Circle uses `HEAD` to validate the
          URL when you create or update a subscription, and `POST` to deliver
          notifications.
        * Responds to `POST` requests with a `200 OK` status code so Circle treats the
          delivery as successful. Any other status causes Circle to retry the
          notification.

        To test before deploying a real endpoint, generate a temporary URL with
        [webhook.site](https://webhook.site/) and use it as your subscriber endpoint.
      </Step>

      <Step title="Allowlist Circle's source IP addresses">
        Configure your firewall, load balancer, or cloud security groups so your
        endpoint only trusts webhook requests from Circle's source IP addresses. This
        blocks unauthenticated traffic at the network edge as a layer of defense in
        addition to [signature verification](/api-reference/verify-webhook-signatures).
        Allowlist the IP addresses for each product you integrate with separately.

        **Wallets, Contracts, and Gateway** share the same webhook delivery
        infrastructure:

        * `54.243.112.156`
        * `100.24.191.35`
        * `54.165.52.248`
        * `54.87.106.46`

        **Circle Payments Network (CPN):**

        * `35.169.154.32`
        * `3.90.127.28`
        * `3.230.111.7`
        * `54.88.227.75`

        **Stablecoin FX (StableFX):**

        * `3.230.111.7`
        * `3.90.127.28`
        * `35.169.154.32`
        * `54.88.227.75`
      </Step>

      <Step title="Subscribe to notifications">
        Register your endpoint as a subscriber by calling the Create Subscription
        endpoint for your product. Select your product below for the request shape:

        <Tabs>
          <Tab title="Wallets and Contracts">
            Wallets and Contracts share the same subscription endpoint. See the Create
            Subscription reference for
            [Wallets](/api-reference/wallets/common/create-subscription) or
            [Contracts](/api-reference/contracts/common/create-subscription) for the full
            schema.

            ```bash theme={null}
            curl --request POST \
              --url https://api.circle.com/v2/notifications/subscriptions \
              --header "Authorization: Bearer $CIRCLE_API_KEY" \
              --header "Content-Type: application/json" \
              --data '{
                "endpoint": "https://your-app.example.com/webhooks",
                "notificationTypes": ["*"]
              }'
            ```

            Example response:

            ```json theme={null}
            {
              "data": {
                "id": "b3d9d2d5-4c12-4946-a09d-953e82fae2b0",
                "name": "Transactions Webhook",
                "endpoint": "https://your-app.example.com/webhooks",
                "enabled": true,
                "createDate": "2026-01-15T21:47:35.107250Z",
                "updateDate": "2026-01-15T21:47:35.107250Z",
                "notificationTypes": ["*"],
                "restricted": false
              }
            }
            ```
          </Tab>

          <Tab title="CPN">
            CPN requires `name` and `enabled` in the request body in addition to the common
            fields. See the
            [Create Subscription](/api-reference/cpn/common/create-subscription) reference
            for the full schema.

            ```bash theme={null}
            curl --request POST \
              --url https://api.circle.com/v2/cpn/notifications/subscriptions \
              --header "Authorization: Bearer $CIRCLE_API_KEY" \
              --header "Content-Type: application/json" \
              --data '{
                "endpoint": "https://your-app.example.com/webhooks",
                "name": "CPN Webhooks",
                "enabled": true,
                "notificationTypes": ["*"]
              }'
            ```

            Example response:

            ```json theme={null}
            {
              "data": {
                "id": "1609aa1c-510a-448d-b9b9-3a13566ff922",
                "name": "CPN Webhooks",
                "endpoint": "https://your-app.example.com/webhooks",
                "enabled": true,
                "createDate": "2026-01-15T21:47:35.107250Z",
                "updateDate": "2026-01-15T21:47:35.107250Z",
                "notificationTypes": ["*"],
                "restricted": false
              }
            }
            ```
          </Tab>

          <Tab title="StableFX">
            See the [Create Subscription](/api-reference/stablefx/all/create-subscription)
            reference for the full schema.

            ```bash theme={null}
            curl --request POST \
              --url https://api.circle.com/v2/stablefx/notifications/subscriptions \
              --header "Authorization: Bearer $CIRCLE_API_KEY" \
              --header "Content-Type: application/json" \
              --data '{
                "endpoint": "https://your-app.example.com/webhooks",
                "notificationTypes": ["*"]
              }'
            ```

            Example response:

            ```json theme={null}
            {
              "data": {
                "id": "c4d1da72-111e-4d52-bdbf-2e74a2d803d5",
                "name": "Transactions Webhook",
                "endpoint": "https://your-app.example.com/webhooks",
                "enabled": true,
                "createDate": "2026-01-15T21:47:35.107250Z",
                "updateDate": "2026-01-15T21:47:35.107250Z",
                "notificationTypes": ["*"],
                "restricted": false
              }
            }
            ```
          </Tab>

          <Tab title="Gateway">
            Gateway uses a subscription that includes the wallet addresses and blockchain
            domains to monitor. See the
            [Create Subscription](/api-reference/gateway/all/create-permissionless-subscription)
            reference for the full schema.

            ```bash theme={null}
            curl --request POST \
              --url https://api.circle.com/v2/notifications/subscriptions/permissionless \
              --header "Authorization: Bearer $CIRCLE_API_KEY" \
              --header "Content-Type: application/json" \
              --data '{
                "environment": "mainnet",
                "endpoint": "https://your-app.example.com/webhooks",
                "addresses": ["0xYourWalletAddress"],
                "domains": [0],
                "notificationTypes": ["gateway.*"]
              }'
            ```

            Example response:

            ```json theme={null}
            {
              "data": {
                "id": "9d1fa351-b24d-442a-8aa5-e717db1ed636",
                "name": "Gateway Webhooks",
                "endpoint": "https://your-app.example.com/webhooks",
                "environment": "mainnet",
                "enabled": true,
                "addresses": ["0xYourWalletAddress"],
                "domains": [0],
                "notificationTypes": ["gateway.*"],
                "createDate": "2026-01-15T21:47:35.107250Z",
                "updateDate": "2026-01-15T21:47:35.107250Z"
              }
            }
            ```
          </Tab>
        </Tabs>
      </Step>
    </Steps>
  </Tab>

  <Tab title="v1 notifications">
    Used by [Circle Mint](/circle-mint),
    [Digital Asset Accounts](/digital-asset-accounts), and
    [CPN Managed Payments](/cpn/managed-payments).

    <Steps>
      <Step title="Set up a subscriber endpoint">
        Expose a publicly accessible HTTPS endpoint that:

        * Is reachable from the public internet.
        * Handles both `HEAD` and `POST` requests. Circle issues `HEAD` requests as a
          connectivity warmup. SNS deliveries arrive as `POST` requests with the SNS
          message in the request body.
        * Responds with a `2xx` status code so SNS treats the delivery as successful.

        To test before deploying a real endpoint, generate a temporary URL with
        [webhook.site](https://webhook.site/), use it as your subscriber endpoint, and
        copy the `SubscribeURL` from the confirmation message into your browser to
        complete the handshake.
      </Step>

      <Step title="Register your subscription">
        Call `POST /v1/notifications/subscriptions` with your endpoint URL. The same
        request shape works for Circle Mint, Digital Asset Accounts, and CPN Managed
        Payments.

        ```bash theme={null}
        curl --request POST \
          --url https://api.circle.com/v1/notifications/subscriptions \
          --header "Authorization: Bearer $CIRCLE_API_KEY" \
          --header "Content-Type: application/json" \
          --data '{
            "endpoint": "https://your-app.example.com/webhooks"
          }'
        ```

        Example response:

        ```json theme={null}
        {
          "data": {
            "id": "b8627ae8-732b-4d25-b947-1df8f4007a29",
            "endpoint": "https://your-app.example.com/webhooks",
            "subscriptionDetails": [
              {
                "url": "arn:aws:sns:us-west-2:908968368384:sandbox_platform-notifications-topic",
                "arn": "arn:aws:sns:us-west-2:908968368384:sandbox_platform-notifications-topic:fcb4a2c9-9c4f-4706-b312-6b22650f5d17",
                "status": "pending"
              }
            ]
          }
        }
        ```

        The subscription `status` is `pending` until you complete the confirmation
        handshake in the next step. v1 subscriptions deliver all account events;
        filtering by `notificationTypes` isn't supported.

        <Tip>
          Circle Mint customers can also register subscriptions through the [Circle Mint
          console](https://app.circle.com/) under **Developer → Subscriptions**.
        </Tip>
      </Step>

      <Step title="Confirm the subscription">
        After you register the subscription, SNS sends a `POST` to your endpoint with
        `Type: SubscriptionConfirmation`. The body includes a `SubscribeURL`. Open it in
        your browser, or have your endpoint fetch it server-side, to finish the
        handshake. The subscription status moves to `confirmed` and events begin
        flowing.

        Example `SubscriptionConfirmation` payload:

        ```json theme={null}
        {
          "Type": "SubscriptionConfirmation",
          "MessageId": "ddbdcdcf-d36a-45b5-927c-da25b9b009ae",
          "Token": "2336412f37fb687f5d51e6e2425f004aed7b7526d5fae41bc257a0d80532a6820258bf77eb25b90453b863450713a2a5a4250696d725a306ef39962b5b543752c9003e0841c0e61253fd6c517a94edebe44f36c5fe4ba131c8ea5f6f42a43f97f6e1865505e2f29f79a62f89e18f97e03a0dd5d982a7578c8d6e21154163f2d6aae523cff25557f9bc21b2503d413006",
          "TopicArn": "arn:aws:sns:us-west-2:908968368384:sandbox_platform-notifications-topic",
          "Message": "You have chosen to subscribe to the topic arn:aws:sns:us-west-2:908968368384:sandbox_platform-notifications-topic.\nTo confirm the subscription, visit the SubscribeURL included in this message.",
          "SubscribeURL": "https://sns.us-west-2.amazonaws.com/?Action=ConfirmSubscription&TopicArn=...",
          "Timestamp": "2026-04-11T20:50:16.324Z",
          "SignatureVersion": "1",
          "Signature": "...",
          "SigningCertURL": "https://sns.us-west-2.amazonaws.com/SimpleNotificationService-...pem"
        }
        ```
      </Step>

      <Step title="(Optional) Manage subscriptions">
        List active subscriptions with `GET /v1/notifications/subscriptions` and remove
        one with `DELETE /v1/notifications/subscriptions/{id}`. A subscription can be
        deleted only when every entry in `subscriptionDetails[]` is `confirmed`,
        `deleted`, or a mix of the two. A subscription with any `pending` entry cannot
        be deleted. Resolve the pending state first.

        | Environment | Active subscription cap | `pending` auto-removal |
        | ----------- | ----------------------- | ---------------------- |
        | Sandbox     | 3                       | After 30 days          |
        | Production  | 1                       | After 72 hours         |
      </Step>
    </Steps>

    <Note>
      v1 traffic originates from Amazon SNS rather than Circle, so the IP range is
      the published [AWS SNS IP
      range](https://docs.aws.amazon.com/general/latest/gr/aws-ip-ranges.html) and
      not practical to allowlist narrowly. Rely on [signature
      verification](/api-reference/verify-webhook-signatures) to confirm
      authenticity.
    </Note>
  </Tab>
</Tabs>
