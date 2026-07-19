# CONTEXT.md — Ubiquitous Language

Seeded from `docs/PRD.md`, `docs/Phase_1_Feature_Slices.md`, `docs/Phase_3_Circle_Integration_Plan.md`.
Read alongside `docs/adr/`. Terms below are canonical — don't drift to a synonym once one is picked here.

## Tenancy & actors

- **ClientCompanyId** — the tenant identifier (`TenantId`). One per client company, 1:1 with a `SubAccount`. Carried on every persisted record; always sourced from the validated caller credential, never a route/body parameter.
- **SubAccount** — the product's tenant entity, keyed by `ClientCompanyId`. Maps 1:1 to the *active* `EntityRegistration`. Not a literal Circle concept — Circle has no "sub-accounts"; this is our product's name for a segregated institutional client under our one Distributor account.
- **Caller identity** — who is asking (the credential in the request header). Distinct from **target scope** — whose data the request is about (route/query).
- **Admin** — APISO Portal's privileged credential. Maps to no `SubAccount`. Names target scope explicitly; never impersonates a tenant.
- **SubAccount role** — a client company's own `ClientCompanyId` credential; scoped to its own data only.

## Core entities (PRD §3.1, extended by implementation)

- **EntityRegistration** — one submission of institutional-client details to the provider for compliance screening. Exactly one active per `SubAccount`; rejected ones retained as history. Immutable at the provider — no update/delete, only resubmission.
- **Wallet** — the *provider-side* segregated wallet (`walletId`), created when a registration is accepted. Provider metadata lives here.
- **FundAccount** — the *local* balance-holding entity, 1:1 with a `Wallet`. Distinct from `Wallet`: `Wallet` is the provider's record; `FundAccount` is our own ledger's balance holder that mutations (deposits/transfers/redemptions) update directly. (Decided during grilling — see `docs/adr/0002-fundaccount-vs-wallet.md`.)
- **DepositAddress** — permanent blockchain address per (chain, currency) for a wallet. No rotation, no expiry.
- **LinkedBankAccount** — a verified fiat bank account at the **Master Account** level, used as wire source/destination.
- **Recipient** — a registered destination blockchain address for outbound transfers, allowlisted via out-of-band Mint Console approval. Status: `PendingApproval → Active | Denied`. (Not listed in PRD §3.1's table but a first-class Domain entity in implementation — glossary gap closed here.)
- **Transaction** — a row in the local ledger for every deposit, transfer, or redemption. `TransactionType` is `Deposit | Transfer | Redemption` — there is no separate `Mint` type; a fiat-wire deposit that the provider settles by minting USDC is still recorded as `Deposit` (see `docs/adr/0003-transaction-type-mint-folded-into-deposit.md`).
- **Redemption net** — the amount actually wired out: provider-reported when available, otherwise gross minus fees. Ledger always records all three (gross, fees, net); "net" is never absent in our model even when the provider omits it.
- **BalanceSnapshot** — a point-in-time balance per wallet/`FundAccount`, taken on a schedule and after every ledger mutation.
- **WebhookInboxEntry** — a durably stored provider notification (raw payload, dedup key `CircleEventId`, `Attempts`/`LastError`, processing status). Renamed from the earlier glossary term "WebhookEvent" to match the shipped `Application.Webhooks` type (2026-07-17 grilling session) — `WebhookEvent`/`IncomingWebhookEvent` now names only the transient dispatch record passed into `WebhookProcessor.HandleAsync`, not the persisted row.
- **Dead-lettered** (webhook) — a `WebhookInboxEntry` whose `Attempts` has reached the configured retry ceiling without a `Processed` outcome; a state for manual triage, not a separate status value on the entry (`ProcessingResult` stays `"Failed"` — dead-letter is a derived read, `Attempts >= threshold`, not a new enum member).
- **NotificationOutboxEntry** — a pending outbound notification to the internal consumer service, written in the same DB transaction as the state change it announces.
- **AuditRecord** — an immutable, append-only record of every state-changing action.

## Master Account

**Master Account** is the one canonical term for the non-tenant-keyed, top-level Circle Mint account (PRD's "Main (Distributor) account", "Circle Mint primary wallet", and the `/master-account/...` endpoint prefix are the same thing). Use **Master Account** everywhere in code, docs, and conversation; the others are historical PRD phrasing, not alternate ubiquitous terms. See `docs/adr/0004-master-account-naming.md`.

## Provider abstraction naming

Provider-facing identifiers that cross the Domain/Application boundary use **provider-agnostic** names (`ProviderWalletId`, not `CircleWalletId`); the literal provider name ("Circle") is confined to the Infrastructure tier (`CircleSubAccountGateway`, `CircleMintGateway`, `Infrastructure/Providers/Circle/` — flat convention, no per-module subfolder). See `docs/adr/0005-provider-agnostic-naming.md`.

## Lifecycle states

- **SubAccount / EntityRegistration lifecycle**: `Created → PendingCompliance → Active | Rejected`; `Rejected → PendingCompliance` (resubmission); `Active ↔ Disabled` (internal-only overlay, no provider concept).
- **RecipientStatus**: `PendingApproval → Active | Denied`. Product states, not provider literals — the provider uses several not-yet-active literals (and an `inactive` holding period); anything neither `active` nor `denied` is `PendingApproval` in our language.
- **TransactionStatus**: `Pending → Complete | Failed`. The provider's intermediate `running` is still `Pending` in our language — no `Running` state exists in the product.
- **DepositSourceType**: `Wire | OnChain` — which funding path produced a deposit. The two paths arrive via different provider channels but are the same `Deposit` concept locally.
- **LinkedBankAccount verification**: `Pending → Complete | Failed`, provider-driven; a bank account is usable as a redemption destination only when `Complete`.

## Gateways (ports, Application tier)

Two gateway ports, not one — confirmed in `docs/adr/0006-deposit-listing-on-stablecoin-gateway.md`
and `docs/features/07-sub-account-and-entity-registration.md`, not ambiguous:

- **`ISubAccountGateway`** (`Application.Ports`) — entity/registration provider operations only (`CircleSubAccountGateway` / `MockSubAccountGateway`).
- **`IStablecoinGateway`** (`Application.Ports`) — money-moving provider operations: transfers, redemptions, transfer/redemption status, deposit-address generation (`GenerateDepositAddressAsync`), recipient registration (`RegisterRecipientAsync`), and deposit listing (`ListRecentDepositsAsync`) — implemented by `CircleMintGateway` / `MockStablecoinGateway`. Recipients and deposit addresses aren't Compliance-owned, so their gateway calls live here, not on `ISubAccountGateway`. See `docs/adr/0006-deposit-listing-on-stablecoin-gateway.md`.

## Folder structure (superseding note)

There is no module axis (`Compliance`/`Ledger`/`Webhooks`/`Admin`/`Shared`) in this repo —
that was the original B0.5 decision but was superseded 2026-07-19 in favor of flat by-kind
folders (`Handlers/`, `Ports/`, `Dtos/`, `Validators/`, `Services/`). See
`docs/adr/0001-module-boundaries.md` and `ARCHITECTURE.md` for the current structure; treat any
mention above of a module name as ubiquitous-language/domain grouping only, not a namespace.

## Idempotency & error contract

- **Idempotency key** — caller-supplied on every mutating operation; forwarded to the provider on money-moving calls. Reserve → gateway/state-transition → complete, two `SaveChangesAsync` calls.
- **RFC 7807 problem-details taxonomy**: `validation`, `not-found`, `tenant-forbidden`, `conflict`, `provider-rejected`, `provider-unavailable`.

## Mock Mode

A configuration-switched simulated provider inside the API (not a separate service). Structurally impossible to enable in Production — hard environment check, not config alone.
