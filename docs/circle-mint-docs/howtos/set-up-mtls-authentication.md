# How-to: Set up mTLS authentication

> Configure mutual TLS authentication for Circle Mint API calls using a client certificate issued by Circle and an API key.

This guide walks you through configuring mutual TLS (mTLS) for Circle Mint API
calls by pairing a client certificate issued by Circle with a new API key. You
generate a key pair and certificate signing request (CSR) locally, submit the
CSR to Circle, and Circle issues your signed certificate. After completing these
steps, your client presents the certificate during the TLS handshake and an API
key in the HTTP header on every request.

<Note>
  Entities operating in an EU/EEA member state under the Markets in
  Crypto-Assets (MiCA) regulation must use mTLS. Any other Circle Mint customer
  with an active API key can opt in to mTLS for extra security. Until Circle
  enables mTLS on your entity, standard API key authentication continues to work
  with no changes.
</Note>

## Prerequisites

Before you begin, ensure that you've:

* Contacted [Circle Support](https://support.circle.com) or your Circle account
  manager to request that Circle enable mTLS on your entity.
* Installed OpenSSL 1.1.1 or later on your machine for key pair and CSR
  operations.
* Configured access to the [Mint Console](https://app.circle.com) with
  multi-factor authentication (MFA).

For background on mTLS, see
[How mTLS authentication works](/circle-mint/mtls-authentication).

## Steps

<Steps>
  <Step title="Generate a key pair">
    Generate an Elliptic Curve Digital Signature Algorithm (ECDSA) P-256 key
    pair. Circle accepts only ECDSA P-256 keys. RSA keys and other curves are
    rejected.

    ```bash theme={null}
    openssl ecparam -genkey -name prime256v1 -noout -out client-key.pem
    ```

    <Warning>
      Keep your private key (`client-key.pem`) secure and never share it with
      Circle or any third party. Only the CSR, which contains your public key,
      is submitted.
    </Warning>
  </Step>

  <Step title="Generate a CSR">
    Generate a PKCS#10 CSR from your key pair:

    ```bash theme={null}
    openssl req -new -key client-key.pem -out client.csr \
      -subj "/CN=<Your Organization Name>/O=<Your Organization>"
    ```

    Replace `<Your Organization Name>` and `<Your Organization>` with your
    entity's legal name. These values help Circle identify your request. Circle
    assigns the final certificate fields during issuance.
  </Step>

  <Step title="Verify the CSR">
    Before submitting, confirm the CSR uses the correct key type:

    ```bash theme={null}
    openssl req -in client.csr -noout -text
    ```

    The output must show a public key algorithm of `id-ecPublicKey` and an ASN1
    OID of `prime256v1`.
  </Step>

  <Step title="Submit your CSR to Circle">
    Provide the following to [Circle Support](https://support.circle.com) or
    your Circle account manager:

    * Your entity ID, found in the Mint Console under **Settings**.
    * Your CSR file (`client.csr`).
    * A request to enable mTLS on your entity.

    Circle validates your CSR and issues a signed client certificate from its
    private certificate authority (CA).
  </Step>

  <Step title="Receive and verify your signed certificate">
    Circle delivers the following files through a secure, out-of-band channel:

    * `client-cert.pem`: your signed client certificate, valid for 365 days.
    * `ca-chain.pem`: the CA certificate chain.

    Confirm that the issued certificate matches your private key by comparing
    the public key hashes:

    ```bash theme={null}
    openssl ec -in client-key.pem -pubout 2>/dev/null | openssl sha256
    openssl x509 -in client-cert.pem -pubkey -noout 2>/dev/null | openssl sha256
    ```

    Both commands must return the same SHA-256 hash. If they differ, the
    certificate and key do not form a valid pair.
  </Step>

  <Step title="Generate a new API key">
    When Circle enables mTLS on your entity, all existing API keys are revoked
    immediately. You must generate a new key before you can make API calls.

    1. Sign in to the [Mint Console](https://app.circle.com).
    2. Complete the MFA challenge. MFA is required for all API key operations on
       mTLS-enabled entities.
    3. Generate a new API key and store it securely.

    <Note>
      API keys on mTLS-enabled entities have a maximum lifetime of 180 days.
      Plan to rotate your key before it expires. See
      [How-to: Rotate an mTLS API key](/circle-mint/rotate-mtls-api-key) for the
      rotation procedure.
    </Note>
  </Step>

  <Step title="Make an authenticated API call">
    Call the hostname that matches how you enabled mTLS:

    * **MiCA-regulated**: Use the regional EU hostname `api-eu.circle.com` for
      all API traffic. Requests sent to `api.circle.com` are rejected.
    * **Optional (non-MiCA)**: Continue to use the standard hostname
      `api.circle.com`. Your endpoint URLs are unchanged.

    The examples in this step use `api-eu.circle.com`. If you enabled mTLS
    optionally, substitute `api.circle.com`.

    Combine your client certificate and API key in a single request. The
    following example retrieves your account balances:

    ```bash theme={null}
    curl --cert /path/to/client-cert.pem \
         --key /path/to/client-key.pem \
         --request GET \
         --url https://api-eu.circle.com/v1/businessAccount/balances \
         --header "Authorization: Bearer ${YOUR_API_KEY}"
    ```

    To confirm that the TLS handshake is succeeding and that TLS 1.3 is in use,
    add the `-v` (verbose) flag:

    ```bash theme={null}
    curl -v --cert /path/to/client-cert.pem \
         --key /path/to/client-key.pem \
         --request GET \
         --url https://api-eu.circle.com/v1/businessAccount/balances \
         --header "Authorization: Bearer ${YOUR_API_KEY}"
    ```

    Look for `SSL connection using TLSv1.3` in the verbose output. Circle
    supports TLS 1.2 and TLS 1.3, but TLS 1.3 is recommended.
  </Step>

  <Step title="Validate Circle's server certificate">
    Complete the two-way trust relationship by validating the server certificate
    Circle presents during the handshake. How you validate it depends on why you
    enabled mTLS.

    If you enabled mTLS **under MiCA**, Circle presents a Qualified Website
    Authentication Certificate (QWAC) at `api-eu.circle.com`. Choose one of the
    following approaches to validate it.

    **Option A: Use a payment aggregator (recommended)**

    Delegate certificate validation to a payment aggregator. The aggregator
    handles trust chain verification, revocation checks, and PSD2 compliance
    validation on your behalf. This approach reduces integration complexity and
    ongoing maintenance.

    **Option B: Validate directly against EU Trusted Lists**

    If you validate Circle's server certificate directly, your implementation
    must:

    1. **Verify the certificate chain** against the root certificate authorities
       (CAs) published on the
       [EU Trusted Lists](https://eidas.ec.europa.eu/efda/tl-browser/).
    2. **Confirm PSD2 QcStatements** are present in the certificate. These
       statements attest that the certificate is a QWAC issued for PSD2
       purposes.
    3. **Check OCSP revocation status** to ensure the certificate has not been
       revoked.
    4. **Verify the Organization Identifier and NCA** in the certificate match
       Circle's registration details.

    If you enabled mTLS **optionally (non-MiCA)**, Circle presents its standard
    server certificate at `api.circle.com`. Validate it the same way you
    validate any HTTPS connection, using the CA certificate chain
    (`ca-chain.pem`) Circle provides. The QWAC options above don't apply to you.
  </Step>
</Steps>

## Troubleshooting

The following table lists common errors and their causes.

| Error                                          | Cause                                                                                                               | Resolution                                                                                                                                                           |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `401` "Invalid credentials"                    | The API key is invalid, expired, or was revoked when mTLS was enabled.                                              | Generate a new API key from the [Mint Console](https://app.circle.com) with MFA.                                                                                     |
| `403` "Valid mTLS client certificate required" | No client certificate was presented, or the certificate does not match the entity.                                  | Verify that you pass `--cert` and `--key` in your request and that you use the certificate Circle issued for your entity.                                            |
| TLS handshake failure                          | The certificate and private key do not match, the certificate format is unsupported, or the certificate is expired. | Compare the certificate and key public key hashes to verify the cert/key pair, as shown when you received your certificate. Confirm the certificate has not expired. |
| Connection rejected or timeout (MiCA)          | The request was sent to `api.circle.com` instead of the regional EU hostname.                                       | If you enabled mTLS under MiCA, update your integration to call `api-eu.circle.com` for every endpoint.                                                              |
