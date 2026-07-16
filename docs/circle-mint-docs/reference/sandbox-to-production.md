# Sandbox to production

> Transition your Circle Mint integration from the sandbox environment to production.

Verified live 2026-07-07 (current nav label/slug is "Sandbox and Testing" at
https://developers.circle.com/circle-mint/references/sandbox-and-testing —
same content, renamed page) — content below unchanged.

Circle provides a sandbox environment at `https://api-sandbox.circle.com` for
prototyping and integration testing. Sandbox APIs match production APIs, so you
can develop and test without generating real financial transactions. Simulated
transactions use
[test networks only](/stablecoins/usdc-contract-addresses#testnet) and do not
move real funds. For details on idempotent requests, pagination, and date
filtering, see the [API reference](/api-reference/idempotent-requests).

After completing your integration in the sandbox, follow these steps to move to
production. If you have questions, contact your Circle solutions engineer or
[customer support](mailto:customer-support@circle.com).

<Steps>
  <Step title="Replace sandbox URLs with production URLs">
    Update all API base URLs from `https://api-sandbox.circle.com` to
    `https://api.circle.com`.
  </Step>

  <Step title="Replace sandbox API keys with production API keys">
    Use the [Mint Console](https://app.circle.com/) to create a production
    API key.

    <Warning>
      Production API keys allow you to work with real funds. Treat production
      keys with appropriate security protocols. Consult your security team for
      key management best practices.
    </Warning>
  </Step>

  <Step title="(Optional) Set up an IPv4 allowlist">
    If you configure an IP allowlist, Circle only accepts requests using your
    production API key that originate from an IP address on that list. Verify
    that the static addresses in your system match the ones you registered with
    Circle.
  </Step>

  <Step title="Verify API key roles">
    In the sandbox, your API key provides access to all endpoints. In
    production, access is restricted to the APIs your entity is authorized to
    use.

    * Test each API call to confirm it works in production. For example, verify
      that your
      [list all balances](/api-reference/circle-mint/account/list-business-balances)
      call returns results.
    * If you receive `403` responses or need additional capabilities, contact
      your Circle representative.
  </Step>

  <Step title="Allow for production settlement times">
    Sandbox settlement times are kept short for testing convenience. Production
    settlement times reflect real-world processing and are longer. For onchain
    transfers, the time to finality depends on the number of
    [blockchain confirmations](/circle-mint/references/blockchain-confirmations)
    required for each blockchain.

    * Test actual settlement times in production.
    * Decide whether to perform actions (such as releasing goods) after
      confirmation or after settlement.
  </Step>

  <Step title="Anticipate transaction fees">
    Sandbox fees may not reflect your client agreement with Circle.

    * Review your contract with Circle to understand production fees.
    * Test transactions in production to determine actual fees.
    * Update your interface if you pass fees along to your customers.
  </Step>
</Steps>

## Debug requests with API Logs

Circle stores every API request and response your account makes and surfaces
them in the [API Logs page](https://app-sandbox.circle.com/developer/logs) of
the Mint Console (Developer Tab → Logs). Logs are retained for seven days and
include the HTTP status, path, request ID, idempotency key, user agent, origin,
timestamp, and full request and response bodies. You can filter by request ID,
resource ID, idempotency key, date range, status, HTTP method, and path.

Sensitive values—payment methods and personally identifiable information—appear
as `[redacted]` in stored payloads. Contact your Circle account manager if you
need access to redacted data for a specific debugging session.
