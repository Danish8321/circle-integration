# How-to: Rotate an mTLS API key

> Rotate your API key for an mTLS-enabled Circle Mint entity before the mandatory 180-day expiration.

API keys on mTLS-enabled entities carry a maximum lifetime of 180 days, whether
you enabled mTLS optionally or under MiCA. API keys that exceed this limit are
automatically invalidated. This guide walks you through generating a replacement
key and rotating your integration with zero downtime.

## Prerequisites

Before you begin, ensure that you've:

* [Configured mTLS on your entity](/circle-mint/set-up-mtls-authentication) and
  have a working integration.
* Configured access to the [Mint Console](https://app.circle.com) as an
  Administrator with multi-factor authentication (MFA).

## Steps

<Steps>
  <Step title="Generate a new API key">
    1. Sign in to the [Mint Console](https://app.circle.com).
    2. Complete the MFA challenge. MFA is required for all API key operations on
       mTLS-enabled entities.
    3. Generate a new API key and store it securely.

    <Note>
      Generate the new key well in advance of the 180-day expiration. After 180
      days, the old key is automatically invalidated and can no longer
      authenticate requests.
    </Note>
  </Step>

  <Step title="Update the authorization header in your integration">
    Replace the `Authorization: Bearer` header value in your integration with
    the new API key. For example, update the environment variable or secrets
    manager entry that stores your key:

    ```text theme={null}
    YOUR_API_KEY=your-new-api-key
    ```
  </Step>

  <Step title="Verify the new key with a test API call">
    Send a test request using the new API key and your existing client
    certificate to confirm the new key works. The example below uses
    `api-eu.circle.com` (the MiCA-regulated hostname). If you enabled mTLS
    optionally, substitute `api.circle.com`:

    ```bash theme={null}
    curl -v --cert /path/to/client-cert.pem \
         --key /path/to/client-key.pem \
         --request GET \
         --url https://api-eu.circle.com/v1/businessAccount/balances \
         --header "Authorization: Bearer ${YOUR_API_KEY}"
    ```

    Look for `SSL connection using TLSv1.3` in the verbose output and confirm
    you receive a successful response. If you receive a `401` error, verify that
    you copied the new key correctly.
  </Step>

  <Step title="Revoke the old API key">
    After you confirm the new key is working in your integration:

    1. Sign in to the [Mint Console](https://app.circle.com).
    2. Complete the MFA challenge.
    3. Revoke the old API key.

    <Warning>
      Do not revoke the old key until you have verified that the new key works.
      Revoking the old key is irreversible.
    </Warning>
  </Step>
</Steps>
