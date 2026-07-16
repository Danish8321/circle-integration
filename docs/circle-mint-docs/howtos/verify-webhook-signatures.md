# How-to: Verify webhook signatures

> Confirm that a webhook notification was sent by Circle by verifying its digital signature.

Verified live 2026-07-07 at https://developers.circle.com/api-reference/verify-webhook-signatures
(moved out of `circle-mint/*` — product-agnostic v2 signature page, not
Circle-Mint-specific) — content below unchanged. **Circle Mint uses v1
notifications, which this page explicitly does NOT cover** — v1 uses a
different signature scheme (Base64-encoded signature of a canonical message
string + a `SigningCertURL`, SNS-style), and Circle's own docs say to contact
your Circle representative for v1 verification details. Don't apply the
ECDSA/`X-Circle-Signature` flow below to Circle Mint webhooks without
confirming the v1 scheme separately.

Every
[v2 webhook notification](/api-reference/webhooks#notification-api-versions)
sent by Circle (Circle Wallets, Circle Contracts, CPN payments, Gateway,
StableFX) is signed with an asymmetric key. By verifying the signature on each
notification, you confirm the payload came from Circle and wasn't tampered with
in transit. The verification flow is the same across these products. Only the
public key endpoint differs per product.

<Note>
  v1 notifications (Circle Mint, Digital Asset Accounts, CPN Managed Payments)
  use a different signature scheme. Contact your [Circle
  representative](mailto:sales@circle.com) for details.
</Note>

## Signature scheme

Circle signs each v2 webhook notification with the `ECDSA_SHA_256` algorithm.
Every notification includes two headers your endpoint uses to verify the
signature:

* `X-Circle-Signature`: the digital signature of the notification body,
  base64-encoded.
* `X-Circle-Key-Id`: the UUID of the public key that signed the notification.

Each signature is unique to the notification it accompanies, so run the
verification flow below for every webhook you receive.

## Verify a signature

<Steps>
  <Step title="Read the signature and key ID from the headers">
    Extract `X-Circle-Signature` and `X-Circle-Key-Id` from the incoming webhook
    request's headers.

    ```text theme={null}
    X-Circle-Key-Id: 879dc113-5ca4-4ff7-a6b7-54652083fcf8
    X-Circle-Signature: MEYCIQCA9EvPbdEJiy7Cw0eY+KQZA/oFi5ZEInPs8CYpyaJexgIhAKtRNnDz9QRQmFKx8QFrvawp+8b9Bs2dQ03xD+XaWVDE
    ```
  </Step>

  <Step title="Fetch the public key">
    Using the value of `X-Circle-Key-Id`, call your product's public key endpoint to
    retrieve the public key and algorithm. Replace `<public_key_endpoint>` with the
    endpoint for your product:

    * [Wallets](/api-reference/wallets/common/get-notification-signature),
      [Contracts](/api-reference/contracts/common/get-notification-signature), and
      [Gateway](/api-reference/gateway/all/get-permissionless-notification-signature):
      `/v2/notifications/publicKey/{keyId}`
    * [CPN](/api-reference/cpn/common/get-notification-signature):
      `/v2/cpn/notifications/publicKey/{keyId}`
    * [StableFX](/api-reference/stablefx/all/get-notification-signature):
      `/v2/stablefx/notifications/publicKey/{id}`

    ```bash theme={null}
    curl --request GET \
      --url 'https://api.circle.com/<public_key_endpoint>/879dc113-5ca4-4ff7-a6b7-54652083fcf8' \
      --header 'Authorization: Bearer $CIRCLE_API_KEY'
    ```

    A successful response returns the base64-encoded public key:

    ```json theme={null}
    {
      "data": {
        "id": "879dc113-5ca4-4ff7-a6b7-54652083fcf8",
        "algorithm": "ECDSA_SHA_256",
        "publicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAESl76SZPBJemW0mJNN4KTvYkLT8bOT4UGhFhzNk3fJqf6iuPlLQLq533FelXwczJbjg2U1PHTvQTK7qOQnDL2Tg==",
        "createDate": "2026-01-15T21:47:35.107250Z"
      }
    }
    ```

    <Tip>
      The public key for a given `keyId` is static, so cache the result to avoid
      fetching it on every webhook.
    </Tip>
  </Step>

  <Step title="Verify the signature against the raw body">
    Use the public key to verify the signature against the **raw** request body.
    Parsing the JSON and re-serializing it changes the byte order, so the signature
    no longer matches.

    <CodeGroup>
      ```typescript Node.js theme={null}
      import { createVerify, createPublicKey, KeyObject } from "crypto";

      // Cache the public key by keyId to avoid refetching on every webhook.
      const publicKeyCache = new Map<string, KeyObject>();

      async function getPublicKey(keyId: string): Promise<KeyObject> {
        const cached = publicKeyCache.get(keyId);
        if (cached) return cached;

        // Replace <public_key_endpoint> with your product's endpoint.
        const response = await fetch(
          `https://api.circle.com/<public_key_endpoint>/${keyId}`,
          { headers: { Authorization: `Bearer ${process.env.CIRCLE_API_KEY}` } },
        );
        const { data } = await response.json();

        const publicKey = createPublicKey({
          key: Buffer.from(data.publicKey, "base64"),
          format: "der",
          type: "spki",
        });
        publicKeyCache.set(keyId, publicKey);
        return publicKey;
      }

      export async function verifyWebhook(
        rawBody: string,
        signature: string,
        keyId: string,
      ): Promise<boolean> {
        const publicKey = await getPublicKey(keyId);
        const verifier = createVerify("SHA256");
        verifier.update(rawBody);
        return verifier.verify(publicKey, signature, "base64");
      }
      ```

      ```python Python theme={null}
      import base64
      import os
      import requests

      from cryptography.exceptions import InvalidSignature
      from cryptography.hazmat.primitives import hashes, serialization
      from cryptography.hazmat.primitives.asymmetric import ec

      # Cache the public key by keyId to avoid refetching on every webhook.
      public_key_cache: dict = {}

      def get_public_key(key_id: str):
          if key_id in public_key_cache:
              return public_key_cache[key_id]

          # Replace <public_key_endpoint> with your product's endpoint.
          response = requests.get(
              f"https://api.circle.com/<public_key_endpoint>/{key_id}",
              headers={"Authorization": f"Bearer {os.environ['CIRCLE_API_KEY']}"},
          )
          data = response.json()["data"]
          public_key_bytes = base64.b64decode(data["publicKey"])
          public_key = serialization.load_der_public_key(public_key_bytes)
          public_key_cache[key_id] = public_key
          return public_key

      def verify_webhook(raw_body: bytes, signature_b64: str, key_id: str) -> bool:
          public_key = get_public_key(key_id)
          signature_bytes = base64.b64decode(signature_b64)
          try:
              public_key.verify(
                  signature_bytes,
                  raw_body,
                  ec.ECDSA(hashes.SHA256()),
              )
              return True
          except InvalidSignature:
              return False
      ```
    </CodeGroup>

    If verification succeeds, the notification is authentic. If it fails, reject the
    request.
  </Step>
</Steps>
