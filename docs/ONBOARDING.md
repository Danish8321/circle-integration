# Developer onboarding

Welcome. This doc gets you from clone to your first shipped slice. Read it top to
bottom once, then keep it as a reference.

## 1. Read these first, in order

1. `ARCHITECTURE.md` (repo root) — how the repo is laid out, one request traced
   end to end. Read this before opening any code.
2. `CONTEXT.md` (repo root) — ubiquitous language (glossary). Terms here are
   canonical; don't invent synonyms.
3. `.claude/CLAUDE.md` — the tier rules, invariants, and scripts you'll run.
4. `docs/README.md` — doc map: which of the three doc trees to check for what.

## 2. Local setup

```
git clone <repo>
cd circle-integration
dotnet restore
bash .claude/scripts/run.sh          # starts the API (SQL Server LocalDB dev)
```

In Development, browse `/scalar` for an interactive API reference (Scalar UI over
the same `/openapi/v1.json` document `MapOpenApi()` emits) — no separate Swagger
UI package, both routes are Development-only.

Scripts you'll use constantly (all under `.claude/scripts/`):

| Script | What it does |
|---|---|
| `check.sh [$FILE]` | Build one project with warnings-as-errors + analyzers. Run this (or let the post-edit hook run it) after every change. |
| `test-fast.sh` | Unit tests only, <60s. Run before every commit. |
| `test-full.sh` | Integration/e2e against Testcontainers SQL Server. Slower; run before a PR. |
| `schema.sh new\|apply\|verify` | The *only* way to touch the DB schema. Never hand-edit an applied migration. |
| `contract.sh` | Emits `docs/openapi/openapi.json`. Run after any Api DTO/route change. |

## 3. What this system is

TreasuryServiceOrchestrator is a **provider-agnostic treasury orchestration API**.
Internal applications manage stablecoin treasury operations — minting, redemption,
on-chain transfers, fiat/stablecoin deposits — for institutional **sub-accounts**,
through one REST interface. Circle Mint (as a Circle Mint *Distributor*) is the
first provider; the product is defined in capability terms, not Circle's own
endpoint shapes, so a future provider can slot in behind the same ports.

**Tenancy model:** each client company is a `SubAccount`, keyed by
`ClientCompanyId`. Tenant identity always comes from the validated caller
credential (`ICallerContext`), never a route or body parameter — cross-tenant
access is structurally impossible at the data-access layer (see
`docs/features/01-tenancy-and-authorization.md`).

## 4. The shape of every tier (one-paragraph recap)

Pure Clean Architecture, folders by **kind**, not by business module:

```
Domain (flat)  <-  Application/{Handlers,Ports,Dtos,Validators,Services,Exceptions}
                          <-  Infrastructure/{Persistence,Providers/Circle,Mocks,Webhooks,...}
Api/{Controllers,Dtos,Validators,Middleware,DependencyInjection} wires all three.
```

Full detail + a traced `POST /v1/sub-accounts` request: `ARCHITECTURE.md`.

---

## 5. Application flow, feature by feature

Every mutating flow below follows the same shape (invariant #11):
**reserve → gateway/state-transition → complete**, two `SaveChangesAsync` calls,
an idempotency key required on the request and forwarded to the provider on
money-moving calls. Once you've read one flow closely, the rest are the same
shape with different entities.

### 5.1 Sub-account onboarding (compliance)

**Use case:** onboard a new institutional client before they can transact.

```
POST /v1/sub-accounts
{ "clientCompanyId": "...", "legalName": "...", "registrationDetails": {...} }
```

- `Api/Controllers/SubAccountsController.cs` — admin-only, thin: build
  `CreateSubAccountCommand`, dispatch, return `CreateSubAccountResponse`.
- `Application/Handlers/CreateSubAccountHandler.cs` —
  reserve: `SubAccount.Create` (state `Created`) + audit `"SubAccountRequested"` →
  gateway: `ISubAccountGateway.CreateExternalEntityAsync` (Circle "External
  Entity" registration) →
  complete: `SubAccount.BeginCompliance` (`Created → PendingCompliance`),
  `EntityRegistration.Create` (`Pending`).
- Provider decision arrives later via webhook (`ExternalEntitiesWebhookTopicProcessor`)
  or manual resubmission: `POST /v1/sub-accounts/{clientCompanyId}/registrations`
  (`ResubmitEntityRegistrationHandler`) if the first submission was rejected.
- Lifecycle: `Created → PendingCompliance → Active | Rejected`; `Rejected` can
  resubmit back to `PendingCompliance`. `Active ↔ Disabled` is an internal-only
  overlay (`PUT /v1/sub-accounts/{clientCompanyId}/disabled`) — no provider concept.

**Example:** a new client company signs up. Ops calls `POST /v1/sub-accounts`
with their legal details. The sub-account sits `PendingCompliance` until Circle's
compliance team approves it (webhook flips it to `Active`) — only then can the
client generate deposit addresses or move money.

### 5.2 Deposit addresses & funding (deposits are on-chain or wire, fiat-only)

**Use case:** give a sub-account a permanent address to receive stablecoin, or a
wire path to fund via fiat.

```
POST /v1/sub-accounts/{subAccountId}/deposit-addresses   { "chain": "ETH", "currency": "USD" }
GET  /v1/sub-accounts/{subAccountId}/deposit-addresses
```

- `GenerateDepositAddressCommandHandler` — one permanent address per
  (chain, currency) pair, no rotation/expiry. Calls
  `IStablecoinGateway.GenerateDepositAddressAsync`.
- Deposits themselves aren't triggered by an endpoint — they're detected by
  polling (`DepositReconciliationBackgroundService` → `ListRecentDepositsAsync`)
  or by webhook (`DepositsWebhookTopicProcessor`), then posted through
  `ProcessDepositCommandHandler` → `LedgerPostingService`, producing a
  `Transaction` (`TransactionType.Deposit`) and a `BalanceSnapshot`.
- `DepositSourceType` is `Wire | OnChain` — same `Deposit` concept regardless of
  path (see `CONTEXT.md`).

**Example:** a client wants to fund their sub-account. They call
`POST .../deposit-addresses` once to get a permanent ETH/USDC address, hand it to
their counterparty, and the reconciliation job picks up the on-chain deposit
within its polling interval — no further API call needed on the client's part.

### 5.3 Outbound transfers & recipients (Travel Rule via identity, not fields)

**Use case:** move stablecoin from a sub-account to a pre-approved on-chain
destination.

```
POST /v1/sub-accounts/{subAccountId}/recipients        (register + allowlist a destination)
POST /v1/sub-accounts/{subAccountId}/transfers          (move funds to an Active recipient)
GET  /v1/sub-accounts/{subAccountId}/transfers/{transferId}
```

- `RegisterRecipientCommandHandler` — `Recipient` starts `PendingApproval`, must
  reach `Active` (via Mint Console, out-of-band) before it's usable as a
  transfer destination.
- `CreateTransferCommandHandler` — reserve (`Transaction` row,
  `TransactionType.Transfer`, `Pending`) → gateway
  (`IStablecoinGateway`, no Travel Rule originator fields on the request —
  satisfied structurally via account-on-file identity + recipient verification,
  invariant #12) → complete on provider status callback
  (`ProcessTransferStatusCommandHandler`, mapped by `TransferStatusMapper`).

**Example:** a client wants to pay a vendor in USDC. They first register the
vendor's wallet as a `Recipient` (status `PendingApproval`), wait for Mint
Console approval (`Active`), then call `POST .../transfers` — attempting a
transfer to a non-`Active` recipient is rejected before any gateway call.

### 5.4 Linked bank accounts, wire instructions, redemptions

**Use case:** convert stablecoin back to fiat and wire it out.

```
POST /v1/sub-accounts/{subAccountId}/linked-bank-accounts
GET  /v1/sub-accounts/{subAccountId}/linked-bank-accounts/{id}/wire-instructions
POST /v1/sub-accounts/{subAccountId}/redemptions
```

- A `LinkedBankAccount` is provider-verified (`Pending → Complete | Failed`) at
  the **Master Account** level before it's usable as a redemption destination.
- `CreateRedemptionCommandHandler` — reserve → gateway → complete, same shape as
  a transfer. `RedeemRequest.Net` (the amount actually wired) is always recorded:
  provider-reported when available, otherwise gross minus fees — never absent.
- Status settles via webhook (`PayoutsWebhookTopicProcessor` →
  `ProcessPayoutStatusCommandHandler`).

**Example:** a client wants to cash out. Their bank account must already be a
`Complete` `LinkedBankAccount`; they call `POST .../redemptions` with an amount,
the ledger reserves the debit immediately, and the wire settles asynchronously —
the client polls `GET .../redemptions/{id}` or waits for their own webhook
consumer to be notified via the notification outbox (§5.6).

### 5.5 Balances, transactions, ledger

**Use case:** show a client their current/historical balance and transaction log.

```
GET /v1/sub-accounts/{subAccountId}/balances
GET /v1/sub-accounts/{subAccountId}/balances/history
GET /v1/sub-accounts/{subAccountId}/transactions
GET /v1/sub-accounts/{subAccountId}/transactions/{transactionId}
```

- Every mutation above (deposit, transfer, redemption) posts through
  `LedgerPostingService`, which is the single writer of `Transaction` rows and
  triggers a `BalanceSnapshot`. Reads never recompute from raw provider state —
  they read the local ledger, so a webhook outage doesn't corrupt what a client
  sees, it just delays it.
- `ScheduledBalanceSnapshotBackgroundService` also takes a snapshot on a fixed
  schedule, independent of mutations, so balance history has regular points
  even during quiet periods.

### 5.6 Webhooks & notifications (Circle in, internal consumer out)

**Use case:** react to provider async state changes, and tell *our own* internal
consumer service when something changed.

```
POST /v1/webhooks/circle          (Circle -> us: SNS-delivered events)
POST internal/notifications        (stub: us -> internal consumer, dev-only)
POST /v1/admin/webhooks/{id}/replay
POST /v1/admin/notifications/{id}/replay
```

- `CircleWebhooksController` verifies the SNS signature
  (`ISnsSignatureVerifier`), writes a durable `WebhookInboxEntry` (dedup key
  `CircleEventId`) before doing anything else, then dispatches to the matching
  `IWebhookTopicProcessor` (one per Circle topic: externalEntities, deposits,
  addressBookRecipients, transfers, wire, payouts).
- A `WebhookInboxEntry` that exhausts its retry ceiling is **dead-lettered** —
  not a separate status value, just `Attempts >= threshold` on a `"Failed"`
  entry — and needs a manual `POST /v1/admin/webhooks/{id}/replay`.
- Every state change that matters to the internal consumer also writes a
  `NotificationOutboxEntry` in the *same DB transaction* as the state change —
  so a notification is never lost even if the dispatcher is down; it just
  retries or gets manually replayed the same way.

**Example:** Circle approves a compliance registration. Circle POSTs to
`/v1/webhooks/circle`; `ExternalEntitiesWebhookTopicProcessor` flips the
`SubAccount`/`EntityRegistration` to `Active` and — in the same transaction —
queues a `NotificationOutboxEntry` so the internal consumer service finds out
without polling.

### 5.7 Admin cross-tenant views

**Use case:** ops needs to see across all tenants — never *as* a tenant.

```
GET /v1/admin/transactions
GET /v1/admin/master-account/summary
```

- Admin authenticates as itself (`CallerRole.Admin`), never impersonates a
  `ClientCompanyId` — all-tenant access is itself audited (invariant #8).
- `GetMasterAccountSummaryQueryHandler` reports the Circle **Master Account**
  (the one non-tenant-keyed top-level account) — not a per-tenant balance.

### 5.8 Mock mode (local/dev without hitting Circle)

**Use case:** develop and test without live Circle credentials or Testcontainers.

- Gateway selection is environment-gated in
  `Api/DependencyInjection/CircleIntegrationServiceCollectionExtensions.cs`:
  mock mode (`MockSubAccountGateway`/`MockStablecoinGateway`, config-flagged) →
  Development-without-mock (`FakeSubAccountGateway`/`FakeStablecoinGateway`,
  both fake together — see the note in that file, a fake sub-account gateway
  paired with the real mint gateway would issue live money-moving calls
  against sub-accounts Circle never saw) → Production (real Circle gateways,
  resilient `HttpClient`).
- Mock mode is **structurally impossible to enable in Production** —
  `MockModeGuard.Validate` hard-checks the environment, not config alone
  (`docs/features/02-mock-mode.md`).

---

## 6. Adding a new use-case (the pattern you'll repeat)

1. `Application/Dtos/<UseCase>Command.cs` (or `Query`) + `<UseCase>Result.cs`.
2. `Application/Handlers/<UseCase>Handler.cs` implementing `ICommandHandler`/`IQueryHandler`.
3. If it needs a new external call: `Application/Ports/I<Name>.cs` (interface only).
4. Implement the port: `Infrastructure/Persistence/…Repository.cs` or
   `Infrastructure/Providers/Circle/…Gateway.cs`.
5. Wire the handler + any new port implementation:
   `Api/DependencyInjection/ApplicationServiceCollectionExtensions.cs` /
   `InfrastructureServiceCollectionExtensions.cs`.
6. `Api/Controllers/<Thing>Controller.cs` (thin) + request/response records in
   `Api/Dtos/`. Validator in `Api/Validators/` if the request needs one — the
   global `ValidationActionFilter` picks it up automatically, don't hand-roll it.
7. Tests: unit test the handler in
   `tests/TreasuryServiceOrchestrator.UnitTests/Application/Handlers/` (mock the
   ports), add an architecture-invariant check in `ArchitectureTests` only if
   you're introducing a new structural rule.
8. `bash .claude/scripts/check.sh` → `test-fast.sh` → (if you touched a route/DTO)
   `contract.sh` → (if you touched the schema) `schema.sh new` and read the
   generated migration before `apply`.

## 7. Where to go next

- Full endpoint-by-endpoint design + Circle API mapping for each feature area:
  `docs/features/*.md` (indexed in `docs/README.md`).
- Why the repo is shaped this way: `docs/adr/0001-module-boundaries.md`.
- Raw Circle API reference (exact request/response fields): `docs/circle-mint-docs/`.
