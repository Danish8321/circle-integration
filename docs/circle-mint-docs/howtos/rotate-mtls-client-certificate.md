# How-to: Rotate an mTLS client certificate

> Rotate the client certificate for an mTLS-enabled Circle Mint entity before it expires.

Confirmed live 2026-07-16 at https://developers.circle.com/circle-mint/rotate-mtls-certificate
— content matches. Note: the live slug is `rotate-mtls-certificate` (no
"client"); this file is misnamed relative to the live page, kept as-is here
since renaming is a filename-only concern, not a content error.

Client certificates issued by Circle are valid for 365 days. This guide walks
you through generating a new key pair and certificate signing request (CSR),
obtaining a renewed certificate from Circle, and rotating your integration with
zero downtime.

## Prerequisites

Before you begin, ensure that you've:

* [Configured mTLS on your entity](/circle-mint/set-up-mtls-authentication) and
  have a working integration.
* Installed OpenSSL 1.1.1 or later on your machine for key pair and CSR
  operations.

## Steps

<Steps>
  <Step title="Generate a new key pair">
    Generate a new Elliptic Curve Digital Signature Algorithm (ECDSA) P-256 key
    pair. Circle accepts only ECDSA P-256 keys. RSA keys and other curves are
    rejected.

    ```bash theme={null}
    openssl ecparam -genkey -name prime256v1 -noout -out new-client-key.pem
    ```

    <Warning>
      Keep your private key (`new-client-key.pem`) secure and never share it
      with Circle or any third party. Only the CSR, which contains your public
      key, is submitted.
    </Warning>
  </Step>

  <Step title="Generate a new CSR">
    Generate a PKCS#10 CSR from your new key pair:

    ```bash theme={null}
    openssl req -new -key new-client-key.pem -out new-client.csr \
      -subj "/CN=<Your Organization Name>/O=<Your Organization>"
    ```

    Start this process at least two weeks before your current certificate
    expires to allow time for Circle to process the request and for you to test
    the new certificate.
  </Step>

  <Step title="Submit the CSR and receive your renewed certificate">
    Provide your entity ID and new CSR file (`new-client.csr`) to
    [Circle Support](https://support.circle.com) or your Circle account manager,
    and request a renewed client certificate.

    Circle issues a renewed certificate from its private certificate authority
    (CA) and delivers `new-client-cert.pem` and the CA certificate chain through
    a secure, out-of-band channel.

    Confirm that the renewed certificate matches your new private key by
    comparing the public key hashes:

    ```bash theme={null}
    openssl ec -in new-client-key.pem -pubout 2>/dev/null | openssl sha256
    openssl x509 -in new-client-cert.pem -pubkey -noout 2>/dev/null | openssl sha256
    ```

    Both commands must return the same SHA-256 hash. If they differ, the
    certificate and key do not form a valid pair.
  </Step>

  <Step title="Update the certificate and key paths in your integration">
    Point your integration to the new certificate and key files. Update the
    `--cert` and `--key` paths (or the equivalent configuration in your HTTP
    client) to reference the new PEM files.
  </Step>

  <Step title="Verify the new certificate with a test API call">
    Send a test request using the new certificate and your current API key. The
    example below uses `api-eu.circle.com` (the MiCA-regulated hostname). If you
    enabled mTLS optionally, substitute `api.circle.com`:

    ```bash theme={null}
    curl -v --cert /path/to/new-client-cert.pem \
         --key /path/to/new-client-key.pem \
         --request GET \
         --url https://api-eu.circle.com/v1/businessAccount/balances \
         --header "Authorization: Bearer ${YOUR_API_KEY}"
    ```

    Look for `SSL connection using TLSv1.3` in the verbose output and confirm
    you receive a successful response.
  </Step>

  <Step title="Decommission the old certificate">
    After you verify that the new certificate works in your integration:

    1. Remove the old certificate and key files from your servers.
    2. Securely delete the old private key material.
  </Step>
</Steps>
