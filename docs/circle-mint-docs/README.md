# Circle Mint Developer Docs

Local mirror of Circle's Circle Mint documentation, split one Markdown file
per page. Originally restructured from `docs/Circle_LLM_text.txt` (a raw
LLM-crawler dump, 29 page-fetches). Refreshed 2026-07-07 against the live
site at https://developers.circle.com/circle-mint — the official nav was
reorganized since the original crawl; see "Refresh notes" below.

Source of truth: https://developers.circle.com/llms.txt lists every current
page path. Re-check that index before trusting any page here as current —
Circle's docs site restructures without redirects for old crawl paths.

## Overview

- [Circle Mint](circle-mint.md) — landing page: mint/redeem USDC and EURC overview

## Concepts

- [How minting and redemption works](concepts/how-minting-works.md) — mental model, account structure, Travel Rule split (business transfers vs third-party payouts)
- [Institutional API](concepts/institutional-api.md) — Distributor/External Entity model

## Quickstarts

- [Getting started with the Circle Mint APIs](quickstarts/getting-started.md) — sandbox signup, API key, connectivity check
- [Mint and redeem USDC](quickstarts/mint-and-redeem-usdc.md) — full mint→transfer→redeem cycle
- [Withdraw fiat to bank](quickstarts/withdraw-to-bank.md) — redeem to fiat and send to a linked bank account

## How-tos

- [Receive a stablecoin payin](howtos/receive-stablecoin-payin.md) — accept onchain USDC/EURC via payment intents
- [Transfer USDC onchain](howtos/transfer-usdc-onchain.md)
- [Manage institutional subaccounts](howtos/manage-institutional-subaccounts.md)
- [Set up mTLS authentication](howtos/set-up-mtls-authentication.md) — **likely deprecated/merged**, see Refresh notes
- [Rotate an mTLS API key](howtos/rotate-mtls-api-key.md) — **likely deprecated/merged**
- [Rotate an mTLS client certificate](howtos/rotate-mtls-client-certificate.md) — **likely deprecated/merged**
- [Set up a webhook endpoint](howtos/set-up-a-webhook-endpoint.md) — confirmed live at `api-reference/webhooks`, v1/v2 split, Circle Mint v1 flow accurate
- [Verify webhook signatures](howtos/verify-webhook-signatures.md) — confirmed live at `api-reference/verify-webhook-signatures`, **but v2/ECDSA scheme doesn't apply to Circle Mint** (v1) — see file note

## Reference

- [Supported chains and currencies](reference/supported-chains-and-currencies.md) — current chain/currency support matrix
- [Blockchain confirmations](reference/blockchain-confirmations.md)
- [CAMT.053 daily statements](reference/camt053-daily-statements.md) — confirmed live at `circle-mint/references/camt053-statements`
- [Supported payment rails](reference/supported-payment-rails.md)
- [Webhook notifications](reference/webhook-notifications.md)
- [Error codes](reference/error-codes.md)
- [Sandbox to production](reference/sandbox-to-production.md) — current nav label "Sandbox and Testing"
- [Travel rule compliance](reference/travel-rule-compliance.md) — endpoint is `POST /v1/payouts` (crypto payout), confirmed live
- [How mTLS authentication works](reference/mtls-authentication-overview.md) — **likely deprecated/merged**
- [API keys](reference/api-keys.md) — confirmed live at `api-reference/keys`
- [Idempotent requests](reference/idempotent-requests.md) — confirmed live at `api-reference/idempotent-requests`
- [OpenAPI Specifications](reference/openapi-specifications.md) — confirmed live; use this to fetch definitive endpoint lists (`payouts.yaml`, `account.yaml`, etc.)
- [Webhooks](reference/webhooks-overview.md) — confirmed live at `api-reference/webhooks`

## Refresh notes (2026-07-07)

Two refresh passes done against the live site. Pages marked **stale/unverified**
above 404'd under every path guessed during this refresh; not confirmed
removed — just not found. Re-fetch `https://developers.circle.com/llms.txt`
and search by topic to locate their current path before relying on them.

**Confirmed live and current** (content in this mirror matches, no changes
needed unless noted):

- `circle-mint/introducing-circle-mint` (landing)
- `circle-mint/getting-started-with-the-circle-apis` (new page, added)
- `circle-mint/crypto-payments-quickstart` — payin quickstart nav entry;
  canonical how-to content lives at `receive-stablecoin-payin.md` (added)
- `circle-mint/quickstart-withdraw-to-bank` (new page, added)
- `circle-mint/supported-chains-and-currencies` (new page, added)
- `circle-mint/references/error-codes`
- `circle-mint/references/travel-rule-compliance` — confirmed `POST /v1/payouts`
  is the real, current crypto/stablecoin payout endpoint with Travel Rule
  fields (`source.identities[]`, `purposeOfTransfer`, `address_book`
  destination). Distinct from `POST /v1/businessAccount/payouts`, which is
  fiat wire redemption only (no Travel Rule fields, no `address_book`).
- `circle-mint/references/webhook-notifications`
- `circle-mint/references/blockchain-confirmations`
- `circle-mint/references/supported-payment-rails`
- `circle-mint/references/sandbox-and-testing` — same content as this
  mirror's `sandbox-to-production.md`, just renamed
- `circle-mint/howtos/manage-institutional-subaccounts`
- `circle-mint/concepts/how-minting-works` (new page, added)
- `circle-mint/concepts/institutional-api` (new page, added)
- `api-reference/webhooks` and `api-reference/webhook-endpoints` — confirms a
  v1/v2 notification split: Circle Mint uses **v1**
  (`POST /v1/notifications/subscriptions` + SNS handshake), while
  Wallets/Contracts/CPN/Gateway/StableFX use **v2** (subscription API + IP
  allowlist). This mirror's `set-up-a-webhook-endpoint.md` and
  `webhooks-overview.md` content matches live, moved out of `circle-mint/*`.
- `api-reference/verify-webhook-signatures` — confirmed live, content matches
  this mirror's `howtos/verify-webhook-signatures.md` exactly. **Important:**
  the ECDSA/`X-Circle-Signature` scheme on that page is v2-only. Circle Mint
  is v1 and uses a different scheme (SNS `Signature` + `SigningCertURL`,
  canonical-message-string based) — Circle's own docs say to contact a Circle
  rep for v1 verification details. Don't implement Circle Mint webhook
  verification against the ECDSA flow.

**API keys — confirmed live 2026-07-07** at `https://developers.circle.com/api-reference/keys`
(moved out of `circle-mint/*`, slug is `keys` not `api-keys` — found via
`llms.txt` substring search, not guessing). Content matches: three key
types (API/client/kit), unchanged.

**CAMT.053 — confirmed live 2026-07-07** at `circle-mint/references/camt053-statements`
(real slug is `camt053-statements`, not `camt053-daily-statements` — my
4 earlier path guesses were all wrong, not a real removal; the "zero hits in
llms.txt" search was a false negative too, likely truncation in the fetched
excerpt). Content matches this mirror exactly, unchanged. Lesson: a 404'd
guess plus a zero-hit `llms.txt` search is *not* strong enough evidence of
removal on its own — get user confirmation or an independent source before
downgrading a page to deprecated/removed.

**OpenAPI Specifications — confirmed live 2026-07-07** at
`circle-mint/references/openapi-specifications` (my earlier 404 guess was
just a wrong slug, not deleted content — apology for the false stale flag).
Every Circle Mint spec is hosted raw at `https://developers.circle.com/openapi/{name}.yaml`:
`account.yaml`, `general.yaml`, `institutional.yaml`, `payments.yaml`,
`payouts.yaml`, `cross-currency.yaml`, `reserve-management.yaml`,
`credit.yaml`, `partner-openapi.yaml`. **This is the fastest way to settle
any endpoint-shape question** — fetch the relevant `.yaml` directly instead
of guessing prose-page paths. Fetching `payouts.yaml` and `account.yaml`
directly confirmed the two-payout-endpoint split once and for all (see
`reference/travel-rule-compliance.md` and `reference/openapi-specifications.md`).

**mTLS — re-verified 2026-07-07, second pass, still not found.** Zero hits
for "mtls" anywhere in `llms.txt`; 7 different path guesses across
`circle-mint/*` and `api-reference/*` all 404'd. Only live mention of mTLS
anywhere is one sentence in `getting-started-with-the-circle-apis`: it's
"available for additional security and may be required for entities under EU
MiCA regulation" — no setup/rotation steps. Treat the mTLS howtos and
`mtls-authentication-overview.md` in this mirror as likely deprecated/merged
away, not just misfiled. If you need mTLS for a MiCA-scoped entity, contact
Circle directly rather than trusting this mirror's mTLS pages.

**Idempotent requests — confirmed live 2026-07-07**, moved from
`circle-mint/references/idempotent-requests` to `api-reference/idempotent-requests`
(now a product-agnostic page, not Circle-Mint-specific). Content unchanged,
stamped in the file.
