# Getting started with the Circle Mint APIs

> Set up a sandbox account, create an API key, and verify connectivity before calling Circle Mint endpoints.

Source: https://developers.circle.com/circle-mint/getting-started-with-the-circle-apis

## Account creation

Register at the sandbox environment first. The sandbox lets you test API
integrations without processing real transactions. Sign up at
`app-sandbox.circle.com/signup`, verify your email, then log in.

## API key generation

Create keys through the Mint Console at `app-sandbox.circle.com/developer`.

Store your API key securely and never expose it in client-side code, public
repositories, or other publicly accessible locations. You can create up to 10
keys per environment and optionally restrict usage by IP address.

## Testing connectivity

Two verification steps:

1. **Ping endpoint** (no auth) — `GET api-sandbox.circle.com/ping`
2. **Configuration endpoint** (auth required) — verifies the API key works and
   returns the `masterWalletId`.

## Authentication

Bearer token over HTTPS: `Authorization: Bearer YOUR_API_KEY`.

## Optional

A TypeScript SDK is available via npm, with examples on Circle's GitHub.

mTLS authentication is available for additional security and may be required
for entities under EU MiCA regulation.
