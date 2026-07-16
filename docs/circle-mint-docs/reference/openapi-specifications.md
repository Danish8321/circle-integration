# OpenAPI Specifications

> Find the machine-readable OpenAPI specification for every Circle API, with direct links to each published spec file.

Verified live 2026-07-16 at https://developers.circle.com/api-reference/openapi-specifications
(corrected slug — the `circle-mint/references/...` path 404s). Content below unchanged (was previously wrongly flagged stale/unverified in
this mirror's README; corrected). Confirmed by fetching `payouts.yaml` and
`account.yaml` directly: `payouts.yaml` defines `POST/GET /v1/payouts` +
`GET /v1/payouts/{id}` (crypto/Travel-Rule payout), `account.yaml` defines
`POST/GET /v1/businessAccount/payouts` + `GET /v1/businessAccount/payouts/{id}`
(fiat wire redemption) plus all the `businessAccount/*` endpoints (wires,
CUBIX, PIX, transfers, deposits, recipient/deposit addresses). This settles
the two-payout-endpoint distinction noted elsewhere in this mirror — both
are real, current, and separately spec'd.

Use these machine-readable OpenAPI specifications to generate client code, drive
API tooling, or give an AI agent accurate request and response shapes. Every API
in this reference has one.

Every spec is hosted at `https://developers.circle.com/openapi/` and served as a
raw YAML file. Add the filename to that base path to fetch a spec directly:

```bash theme={null}
curl https://developers.circle.com/openapi/cctp.yaml
```

## Available specifications

Specs are grouped by the product they power. A few specs power more than one
product, so they appear under each. Where the same filename appears under
multiple products, it's the same file—not a product-specific variant.

| Product                 | Specification                                                                                                  |
| :---------------------- | :------------------------------------------------------------------------------------------------------------- |
| Wallets                 | [`configurations_1.yaml`](https://developers.circle.com/openapi/configurations_1.yaml)                         |
|                         | [`configurations_2.yaml`](https://developers.circle.com/openapi/configurations_2.yaml)                         |
|                         | [`developer-controlled-wallets.yaml`](https://developers.circle.com/openapi/developer-controlled-wallets.yaml) |
|                         | [`user-controlled-wallets.yaml`](https://developers.circle.com/openapi/user-controlled-wallets.yaml)           |
|                         | [`buidl-wallets.yaml`](https://developers.circle.com/openapi/buidl-wallets.yaml)                               |
|                         | [`compliance.yaml`](https://developers.circle.com/openapi/compliance.yaml)                                     |
| Contracts               | [`configurations_2.yaml`](https://developers.circle.com/openapi/configurations_2.yaml)                         |
|                         | [`smart-contract-platform.yaml`](https://developers.circle.com/openapi/smart-contract-platform.yaml)           |
| CCTP                    | [`cctp.yaml`](https://developers.circle.com/openapi/cctp.yaml)                                                 |
| Gateway                 | [`gateway.yaml`](https://developers.circle.com/openapi/gateway.yaml)                                           |
| Circle Mint             | [`account.yaml`](https://developers.circle.com/openapi/account.yaml)                                           |
|                         | [`general.yaml`](https://developers.circle.com/openapi/general.yaml)                                           |
|                         | [`institutional.yaml`](https://developers.circle.com/openapi/institutional.yaml)                               |
|                         | [`payments.yaml`](https://developers.circle.com/openapi/payments.yaml)                                         |
|                         | [`payouts.yaml`](https://developers.circle.com/openapi/payouts.yaml)                                           |
|                         | [`cross-currency.yaml`](https://developers.circle.com/openapi/cross-currency.yaml)                             |
|                         | [`reserve-management.yaml`](https://developers.circle.com/openapi/reserve-management.yaml)                     |
|                         | [`credit.yaml`](https://developers.circle.com/openapi/credit.yaml)                                             |
|                         | [`partner-openapi.yaml`](https://developers.circle.com/openapi/partner-openapi.yaml)                           |
| Circle Payments Network | [`configurations.yaml`](https://developers.circle.com/openapi/configurations.yaml)                             |
|                         | [`cpn-ofi.yaml`](https://developers.circle.com/openapi/cpn-ofi.yaml)                                           |
|                         | [`accounts.yaml`](https://developers.circle.com/openapi/accounts.yaml)                                         |
|                         | [`payments.yaml`](https://developers.circle.com/openapi/payments.yaml)                                         |
|                         | [`payouts.yaml`](https://developers.circle.com/openapi/payouts.yaml)                                           |
|                         | [`managed-payments.yaml`](https://developers.circle.com/openapi/managed-payments.yaml)                         |
| StableFX                | [`stablefx.yaml`](https://developers.circle.com/openapi/stablefx.yaml)                                         |
| xReserve                | [`xreserve.yaml`](https://developers.circle.com/openapi/xreserve.yaml)                                         |
| Digital Asset Accounts  | [`accounts.yaml`](https://developers.circle.com/openapi/accounts.yaml)                                         |
| End User Onboarding     | [`customer-orchestration.yaml`](https://developers.circle.com/openapi/customer-orchestration.yaml)             |
|                         | [`partner-openapi.yaml`](https://developers.circle.com/openapi/partner-openapi.yaml)                           |

<Note>
  Each rendered API reference page also exposes its own source spec: append
  `.md` to any endpoint page URL to see the underlying `openapi` reference in
  its frontmatter.
</Note>
