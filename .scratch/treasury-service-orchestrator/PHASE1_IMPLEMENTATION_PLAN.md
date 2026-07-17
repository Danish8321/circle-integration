# Phase 1 Implementation Plan

Status: draft, awaiting approval. Ticket 01 (foundation: tenancy, SubAccount, webhook pipeline
core) already shipped. This plan covers 02-14 in dependency order. Every task names exact
file(s), the change, and its own verification command per CLAUDE.md's verification contract
(`check.sh`/`test-fast.sh`/`test-full.sh`/`contract.sh`/`schema.sh`). No task is "done" without
its stated verification passing — "it compiles" is not evidence.

Tier boundary discipline (CLAUDE.md): Domain -> Application -> Infrastructure -> Api, arrows
point inward only. Every entity-construction site uses `TimeProvider`, never `DateTime.*Now`.
Every gateway uses `IHttpClientFactory`. Every async handler signature carries
`CancellationToken ct = default`. Every mutating handler follows reserve -> gateway/state-
transition -> complete, two `SaveChangesAsync` calls, forwarded idempotency key (invariant 11) —
except the one documented exception (ticket 07/08's `LinkedBankAccount`, no `ClientCompanyId` to
key by, single `SaveChangesAsync`). `Money(decimal, string)` is the only monetary type crossing
the Domain/Application boundary.

Order: **02 -> 03 -> 04 (+12) -> 05 -> 06 -> 07 (+11) -> 08 -> 09 (+14) -> 10**, with **13**
slotted at 09 (correlation-id header) and left partially open (DB-dependent items blocked,
tracked not built this phase).

---

## Ticket 02 — Mock provider gateway (`.scratch/.../02-mock-provider-gateway.md`)

Spec: `docs/features/02-mock-mode.md`. Blocked by: 01 (shipped).

### 02.1 — Domain-adjacent mock infrastructure options/guard
- Files: `Infrastructure/Mocks/MockProviderOptions.cs`, `Infrastructure/Mocks/MockModeGuard.cs`.
- Change: `MockProviderOptions` (latency/failure-injection knobs, `RedemptionFlatFeeAmount =
  1.50m`, `MainWalletBalanceAmount = 10_000m` — needed later by ticket 08 but declared here since
  this is the one options class). `MockModeGuard.Validate(bool mockModeEnabled, string
  environmentName)` throws `InvalidOperationException` if `mockModeEnabled &&
  environmentName == Environments.Production` — structural guard per CLAUDE.md invariant 9.
- Verify: `check.sh Infrastructure/TreasuryServiceOrchestrator.Infrastructure.csproj`.

### 02.2 — Mock random source (thread-safe)
- Files: `Infrastructure/Mocks/IMockRandomSource.cs`, `Infrastructure/Mocks/SystemRandomSource.cs`.
- Change: `IMockRandomSource` port; `SystemRandomSource` backed by `System.Random.Shared` (ratified
  2026-07-17 — mock gateways are singletons, must be thread-safe, never `new Random()`).
- Verify: `check.sh Infrastructure/TreasuryServiceOrchestrator.Infrastructure.csproj`.

### 02.3 — Shared test-utilities project (test doubles)
- Files: new `tests/TreasuryServiceOrchestrator.TestUtilities/{FixedRandomSource.cs,
  CapturingScheduler.cs}` + new `.csproj`.
- Change: `FixedRandomSource : IMockRandomSource` (deterministic), `CapturingScheduler :
  IMockWebhookScheduler` (records scheduled webhooks for assertion). Ratified 2026-07-17: these
  live here, not per-ticket, since tickets 03-10 reuse them.
- Verify: `check.sh tests/TreasuryServiceOrchestrator.TestUtilities/TreasuryServiceOrchestrator.TestUtilities.csproj`.

### 02.4 — Webhook scheduling/dispatch pipeline
- Files: `Infrastructure/Mocks/{ScheduledMockWebhook,IMockWebhookScheduler,MockWebhookChannel,
  MockWebhookDispatcher,MockWebhookDispatchBackgroundService}.cs`.
- Change: scheduler stages `(topic, payloadJson, deliverAtUtc)`; channel is the in-memory queue;
  dispatcher pulls due items and feeds them through the **real** inbox pipeline (ticket 01's
  `WebhookProcessor`) — mock mode is a producer, not a shortcut. Background service polls on an
  interval.
- Verify: `check.sh Infrastructure/TreasuryServiceOrchestrator.Infrastructure.csproj`; unit tests
  `MockWebhookDispatcherTests` -> `test-fast.sh Infrastructure`.

### 02.5 — Mock gateways
- Files: `Infrastructure/Mocks/{MockSubAccountGateway,MockStablecoinGateway}.cs`.
- Change: `MockSubAccountGateway` uses real Circle status literals (`Pending/Accepted/Rejected`
  case-insensitive via `EntityRegistrationStatusMapper.Map`, correction #1). `MockStablecoinGateway`
  starts with only the ticket-01-scoped surface; `RedeemAsync`/deposit/recipient/transfer methods
  added incrementally by tickets 03/05/06/07 (this task stubs the type, later tickets extend it).
  `RedeemAsync` (once ticket 07 lands) stays pure 1:1 fiat-to-target, no fee simulation — ratified.
- Verify: `MockSubAccountGatewayTests`, `MockStablecoinGatewayTests` -> `test-fast.sh Infrastructure`.

### 02.6 — DI wiring + Production guard test
- Files: `Api/Program.cs`, `appsettings.json`, `appsettings.Development.json`.
- Change: register mock gateways only when `MockMode:Enabled == true`, call `MockModeGuard.Validate`
  at startup before any DI registration branches on it.
- Verify: `MockModeGuardTests`, `MockProviderWiringTests` (integration) ->
  `test-fast.sh Infrastructure` + `test-full.sh`; `check.sh` on `Api.csproj`.

---

## Ticket 03 — Deposit address generation (`.scratch/.../03-deposit-address-generation.md`)

Spec: `docs/features/09-deposits-and-funding.md`. Blocked by: 02.

### 03.1 — Domain entity
- Files: `Domain/DepositAddress.cs`.
- Change: `Id, SubAccountId, Chain, Currency, Address, CircleAddressId?, CreatedAtUtc` — via
  `TimeProvider`.
- Verify: `check.sh Domain/TreasuryServiceOrchestrator.Domain.csproj`.

### 03.2 — Shared chain allow-list option
- Files: `Application/Shared/SupportedChainsOptions.cs`.
- Change: wraps `List<string>` (does not inherit it — design-pass #5), exposes
  `IsSupported(string chain)` only. Default `["ETH"]` (verified live, correction #10).
- Verify: `check.sh Application/TreasuryServiceOrchestrator.Application.csproj`.

### 03.3 — Port + gateway DTOs
- Files: `Application/Ledger/Ports/IDepositAddressRepository.cs`,
  `Application/Ledger/Ports/GatewayDtos.cs` (add `GenerateDepositAddressGatewayRequest`,
  `GeneratedDepositAddress` result — distinct name from the command result, design-pass #6),
  `Application/Ledger/Ports/IStablecoinGateway.cs` (add `GenerateDepositAddressAsync`).
- Verify: `check.sh Application/TreasuryServiceOrchestrator.Application.csproj`.

### 03.4 — Command/query handlers
- Files: `Application/Ledger/DepositAddresses/{GenerateDepositAddressCommand,
  GenerateDepositAddressResult,GenerateDepositAddressCommandValidator,
  GenerateDepositAddressCommandHandler,ListDepositAddressesQuery,
  ListDepositAddressesQueryHandler}.cs`.
- Change: handler reserves a system-generated idempotency key scoped
  `deposit-address:{subAccountId}:{chain}:{currency}` (correction #9 — real Circle call requires
  body `idempotencyKey`, reuse on retry) before the gateway call; `(SubAccountId, Chain, Currency)`
  unique index is separate local dedup.
- Verify: `GenerateDepositAddressCommandHandlerTests` (covers reserve/reuse-on-retry explicitly),
  `ListDepositAddressesQueryHandlerTests` -> `test-fast.sh Application`.

### 03.5 — Mock + real gateway implementations
- Files: `Infrastructure/Mocks/MockStablecoinGateway.cs` (extend),
  `Infrastructure/Providers/Circle/CircleMintGateway.cs` (extend).
- Verify: `test-fast.sh Infrastructure`.

### 03.6 — Persistence + migration
- Files: `Infrastructure/Persistence/DepositAddressRepository.cs`,
  `Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs` (add `DbSet` + mapping +
  unique index).
- Change: run `schema.sh new AddDepositAddress`, **hand-review the generated migration before
  applying** (CLAUDE.md: never edit an already-applied migration, read every generated migration).
- Verify: `schema.sh verify`, then `schema.sh apply` only after hand review; show migration to
  user before applying.

### 03.7 — Api
- Files: `Api/Ledger/DepositAddressesController.cs`, `Api/Program.cs` (DI).
- Verify: `DepositAddressesEndpointsTests` -> `test-full.sh`; `contract.sh` (new endpoints);
  `check.sh` full solution.

---

## Ticket 04 (+12) — Ledger transaction and balance (`.scratch/.../04-ledger-transaction-and-balance.md`, `.../12-ledger-posting-service-design.md`)

Spec: `docs/features/04-ledger-and-balances.md`. Blocked by: 03. Fresh build — no prior `Deposit`
entity exists to supersede (corrected scope).

### 04.1 — Domain entities
- Files: `Domain/{TransactionType,TransactionStatus,Transaction,BalanceSnapshotReason,
  BalanceSnapshot,FundAccount}.cs` (`FundAccount.Balance` fixed to `Money`, not `decimal` —
  design-pass #1, if not already present from ticket 01).
- Verify: Domain tests (`Transaction`/`BalanceSnapshot` invariants) -> `test-fast.sh Domain`.

### 04.2 — Ledger-posting module (ticket 12's ratified shape)
- Files: `Application/Ledger/LedgerPostingService.cs`, `Application/Ledger/LedgerPosting.cs`.
- Change: **single method** `PostAsync(LedgerPosting posting, CancellationToken ct)` — signed
  `Money.Amount` (credit = positive, debit = negative), not split `CreditAsync`/`DebitAsync`
  (ratified 2026-07-17). Internally: post `Transaction`, adjust `FundAccount.Balance`, write
  `BalanceSnapshot(Reason = PostMutation)` — one implementation, all money-moving callers
  (deposit credit, transfer debit, redemption debit) route through this, no hand-rolled triplets.
- Verify: `check.sh Application/TreasuryServiceOrchestrator.Application.csproj`; unit test
  `LedgerPostingServiceTests` -> `test-fast.sh Application`.

### 04.3 — Ports
- Files: `Application/Ledger/Ports/{ITransactionRepository,IBalanceSnapshotRepository,
  IFundAccountRepository}.cs`.
- Verify: `check.sh Application`.

### 04.4 — Deposit processing command
- Files: `Application/Ledger/{ProcessDepositCommand,ProcessDepositResult,
  ProcessDepositCommandValidator,ProcessDepositCommandHandler,
  DepositSourceNotResolvedException}.cs`.
- Change: `DepositSourceType { Wire, OnChain }` (correction #8); routes through
  `LedgerPostingService.PostAsync` for the credit.
- Verify: `ProcessDepositCommandHandlerTests` (two-`SaveChangesAsync` pattern exercised) ->
  `test-fast.sh Application`.

### 04.5 — Query handlers
- Files: `Application/Ledger/{ListTransactionsQuery,ListTransactionsQueryHandler,
  GetTransactionQuery,GetTransactionQueryHandler,GetCurrentBalanceQuery,
  GetCurrentBalanceQueryHandler,GetBalanceHistoryQuery,GetBalanceHistoryQueryHandler}.cs`.
- Change: `GetCurrentBalanceQueryHandler`'s no-`FundAccount` default is `Money.Zero("USDC")`
  (ratified 2026-07-17 — every funded account is USDC in this product).
- Verify: `GetCurrentBalanceQueryHandlerTests` (asserts `USDC` default),
  `GetTransactionQueryHandlerTests`, `ListTransactionsQueryHandlerTests`,
  `GetBalanceHistoryQueryHandlerTests` -> `test-fast.sh Application`.

### 04.6 — Deposits webhook routing
- Files: `Infrastructure/Webhooks/DepositsWebhookTopicProcessor.cs`,
  `Application/Ledger/Ports/IDepositAddressRepository.cs` (add `FindByAddressAsync`),
  `Infrastructure/Persistence/DepositAddressRepository.cs` (implement it).
- Change: `deposits` topic = fiat wire only (correction #5); real SNS envelope shapes
  (correction #7).
- Verify: `DepositsWebhookTopicProcessorTests` -> `test-fast.sh Infrastructure`.

### 04.7 — Persistence + migration
- Files: `Infrastructure/Persistence/{TransactionRepository,BalanceSnapshotRepository}.cs`,
  `DbContext` (new `Transaction`/`BalanceSnapshot`/`FundAccount` `DbSet`s + mapping;
  `Transaction.ProviderReferenceId` unique index).
- Change: `schema.sh new AddLedgerTables` — **additive only, no `Deposit` table exists to drop**
  (corrected premise). Hand-review before apply.
- Verify: `schema.sh verify`, hand-reviewed migration shown to user, then `schema.sh apply`.

### 04.8 — Api
- Files: `Api/Ledger/TransactionsController.cs`, `Api/Ledger/BalancesController.cs` (or combined),
  `CircleWebhooksController` (modify to route `deposits` topic), `Program.cs`.
- Verify: Api integration tests (transaction/balance endpoints, `deposits` webhook routing) ->
  `test-full.sh`; `contract.sh`; `check.sh` full solution.

---

## Ticket 05 — Recipients (`.scratch/.../05-recipients.md`)

Spec: `docs/features/10-outbound-transfers-and-recipients.md`. Blocked by: 04.

### 05.1 — Domain
- Files: `Domain/{RecipientStatus,Recipient}.cs`.
- Verify: `check.sh Domain`.

### 05.2 — Port + gateway additions
- Files: `Application/Ledger/Ports/IRecipientRepository.cs`,
  `Application/Ledger/Ports/{GatewayDtos.cs,IStablecoinGateway.cs}` (add
  `RegisterRecipientAsync`).
- Verify: `check.sh Application`.

### 05.3 — Command/query handlers + status mapper
- Files: `Application/Ledger/Recipients/{RegisterRecipientCommand,RegisterRecipientResult,
  RegisterRecipientCommandValidator,RegisterRecipientCommandHandler,ListRecipientsQuery,
  ListRecipientsQueryHandler,GetRecipientQuery,GetRecipientQueryHandler,RecipientStatusMapper,
  ProcessRecipientDecisionCommand,ProcessRecipientDecisionResult,
  ProcessRecipientDecisionHandler}.cs`.
- Change: `RecipientStatusMapper` never throws on unknown literal — REST enum
  `pending_verification|verification_succeeded|active`, webhook vocabulary
  `pending|inactive|active|denied`; `active`->`Active`, `denied`->`Denied`, else->`PendingApproval`
  (logged). `pending_approval` is not a real literal (correction #1).
- Verify: `RegisterRecipientCommandHandlerTests`, `ProcessRecipientDecisionHandlerTests` (explicit
  unknown-literal-doesn't-throw case) -> `test-fast.sh Application`.

### 05.4 — Webhook processor
- Files: `Application/Webhooks/AddressBookRecipientsWebhookTopicProcessor.cs`.
- Change: real SNS envelope shape, not an invented flat DTO (correction #7).
- Verify: `AddressBookRecipientsWebhookTopicProcessorTests` -> `test-fast.sh`.

### 05.5 — Mock + real gateway
- Files: `Infrastructure/Mocks/MockStablecoinGateway.cs` (extend),
  `Infrastructure/Providers/Circle/CircleMintGateway.cs` (extend).
- Verify: `test-fast.sh Infrastructure`.

### 05.6 — Persistence + migration + Api
- Files: `Infrastructure/Persistence/RecipientRepository.cs`, `DbContext`,
  `Api/Ledger/RecipientsController.cs`, `Program.cs`.
- Change: `schema.sh new AddRecipient`, hand-review before apply.
- Verify: `schema.sh verify`/apply after review; Api integration tests -> `test-full.sh`;
  `contract.sh`; `check.sh` full solution.

---

## Ticket 06 — Outbound transfers (`.scratch/.../06-outbound-transfers.md`)

Spec: `docs/features/10-outbound-transfers-and-recipients.md`. Blocked by: 04 (and consumes 05's
`Recipient`).

### 06.1 — Domain
- Files: `Domain/Transfer.cs`.
- Verify: `check.sh Domain`.

### 06.2 — Port + gateway additions
- Files: `Application/Ledger/Ports/ITransferRepository.cs`,
  `Application/Ledger/Ports/{GatewayDtos.cs,IStablecoinGateway.cs}` (add `CreateTransferAsync`;
  destination shape `{type:"verified_blockchain", addressId: <recipient UUID>}` — correction #4,
  `DestinationRecipientId` maps to `addressId`).
- Verify: `check.sh Application`.

### 06.3 — Command/query handlers
- Files: `Application/Ledger/Transfers/{CreateTransferCommand,CreateTransferCommandValidator,
  CreateTransferCommandHandler,ListTransfersQuery,ListTransfersQueryHandler,GetTransferQuery,
  GetTransferQueryHandler,ProcessTransferStatusCommand,
  ProcessTransferStatusCommandHandler}.cs`.
- Change: no Travel Rule originator fields anywhere (invariant 12). Debit routes through
  `LedgerPostingService.PostAsync` (design-pass #2) — no hand-rolled triplet. `Money`, not
  `decimal`, throughout (design-pass #1).
- Verify: `CreateTransferCommandHandlerTests`, `ListTransfersQueryHandlerTests`,
  `GetTransferQueryHandlerTests`, `ProcessTransferStatusCommandHandlerTests` -> `test-fast.sh Application`.

### 06.4 — Transfers webhook processor (dual-direction)
- Files: `Infrastructure/Webhooks/TransfersWebhookTopicProcessor.cs`.
- Change: branches on direction — incoming to one of our wallets = deposit credit via
  `ProcessDepositCommand` (correction #5, this ticket owns it); outgoing = transfer status update
  via `ProcessTransferStatusCommand`. `running` intermediate maps to `Pending`.
- Verify: `TransfersWebhookTopicProcessorTests` — both branches plus `running` state ->
  `test-fast.sh Infrastructure`.

### 06.5 — Mock + real gateway
- Files: `Infrastructure/Mocks/MockStablecoinGateway.cs` (extend — simulate `running` intermediate
  + `failed` outcome, correction #2), `Infrastructure/Providers/Circle/CircleMintGateway.cs`
  (extend).
- Verify: `MockStablecoinGatewayTransferTests` -> `test-fast.sh Infrastructure`.

### 06.6 — Persistence + migration + Api
- Files: `Infrastructure/Persistence/TransferRepository.cs`, `DbContext`,
  `Api/Ledger/TransfersController.cs`, `Program.cs`.
- Change: `schema.sh new AddTransfer`, hand-review before apply.
- Verify: `schema.sh verify`/apply after review; `TransfersEndpointsTests` -> `test-full.sh`;
  `contract.sh`; `check.sh` full solution.

---

## Ticket 07 (+11) — Redemption and linked bank account (`.scratch/.../07-...md`, `.../11-...md`)

Spec: `docs/features/11-redemption-and-payouts.md`, `docs/features/08-banking-and-wire-instructions.md`.
Blocked by: 06.

### 07.1 — Domain
- Files: `Domain/{LinkedBankAccountStatus,LinkedBankAccount}.cs` (widened shape — ticket 11
  resolution (a): `BeneficiaryName, AccountNumber, RoutingNumber, BankName, BillingName,
  BillingCity, BillingCountry, BillingLine1, BillingPostalCode, BillingLine2?, BillingDistrict?,
  BankAddressCountry, BankAddressBankName?, CircleBankAccountId, Status, timestamps`), modify
  `Domain/RedeemRequest.cs` (gross/fees/net split — `GrossAmount: Money`, `Fees: Money?`,
  `NetAmount: Money?`).
- Verify: `check.sh Domain`.

### 07.2 — Ports + gateway additions
- Files: `Application/Ledger/Ports/ILinkedBankAccountRepository.cs`,
  `Application/Ledger/Ports/{GatewayDtos.cs,IStablecoinGateway.cs}` (add
  `CreateLinkedBankAccountAsync` with widened request DTO, `GetWireInstructionsAsync` — this
  file's own addition per spec §3.2), `IRedeemRequestRepository` (modify for gross/fees/net).
- Verify: `check.sh Application`.

### 07.3 — LinkedBankAccount command/query handlers
- Files: `Application/Ledger/LinkedBankAccounts/{CreateLinkedBankAccountCommand,
  CreateLinkedBankAccountCommandValidator,CreateLinkedBankAccountCommandHandler,
  ListLinkedBankAccountsQuery,GetLinkedBankAccountQuery,GetWireInstructionsQuery,
  ProcessLinkedBankAccountStatusCommand,LinkedBankAccountStatusMapper,
  ProcessLinkedBankAccountStatusCommandHandler}.cs`.
- Change: single `SaveChangesAsync` (documented exception to invariant 11 — no `ClientCompanyId`
  to key an `IdempotencyExecutor` reservation by); `IdempotencyKey` still forwarded to Circle.
  Status mapper: `pending->Pending, complete->Active, failed->Failed`, throws on unrecognized
  (closed vocabulary, unlike recipient's).
- Verify: `CreateLinkedBankAccountCommandHandlerTests`,
  `ProcessLinkedBankAccountStatusCommandHandlerTests`, `GetWireInstructionsQueryHandlerTests`,
  `ListLinkedBankAccountsQueryHandlerTests`/`GetLinkedBankAccountQueryHandlerTests` ->
  `test-fast.sh Application`.

### 07.4 — Redemption command/query handlers
- Files: `Application/Ledger/Redemptions/{CreateRedemptionCommand,
  CreateRedemptionCommandValidator,CreateRedemptionCommandHandler,ListRedemptionsQuery,
  GetRedemptionQuery,ProcessPayoutStatusCommand,ProcessPayoutStatusCommandHandler}.cs`.
- Change: gross reserved (validated), not debited, at creation; debit on `Complete` via
  `LedgerPostingService.PostAsync(new LedgerPosting(fundAccount.Id, -redeemRequest.GrossAmount.
  Amount, ..., TransactionType.Redemption, redeemRequest.CircleRedeemId))` (ratified 2026-07-17 —
  no hand-rolled triplet). `toAmount` optional on webhook — net = `toAmount` when present else
  `amount - fees` (correction #3), computed at the webhook mapping edge only.
- Verify: `CreateRedemptionCommandHandlerTests`, `ProcessPayoutStatusCommandHandlerTests` (explicit
  `toAmount`-present/absent branches, reserved-gross-not-webhook-amount debit) ->
  `test-fast.sh Application`.

### 07.5 — Webhook processors
- Files: `Infrastructure/Webhooks/{WireWebhookTopicProcessor,PayoutsWebhookTopicProcessor}.cs`.
- Change: `wire` topic (async bank-account verification, correction #6) — mock returns `pending`
  on create, emitter schedules `complete`/`failed`. `payouts` topic excludes `toAmount` from the
  required-field check.
- Verify: `WireWebhookTopicProcessorTests`, `PayoutsWebhookTopicProcessorTests` (present/absent
  `toAmount` cases, required-field-missing throws) -> `test-fast.sh Infrastructure`.

### 07.6 — Mock + real gateway
- Files: `Infrastructure/Mocks/MockStablecoinGateway.cs` (extend — `RedeemAsync` pure 1:1, no fee
  simulation; `CreateLinkedBankAccountAsync` deterministic `complete` wire webhook;
  `GetWireInstructionsAsync` deterministic synthetic result), `CircleMintGateway.cs` (extend —
  `source` always explicit on `RedeemAsync`, never omitted; wire-creation body built from the
  widened `LinkedBankAccount` fields).
- Verify: `MockStablecoinGatewayRedeemTests`, `MockStablecoinGatewayLinkedBankAccountTests`,
  `CircleMintGatewayRedeemTests` (asserts `source` always present), `CircleMintGatewayTests` (US
  wire-creation body shape) -> `test-fast.sh Infrastructure`.

### 07.7 — Persistence + migration + Api
- Files: `Infrastructure/Persistence/{RedeemRequestRepository (rewrite),
  LinkedBankAccountRepository (new)}.cs`, `DbContext`,
  `Api/Ledger/{RedemptionsController,LinkedBankAccountsController}.cs`, `Program.cs`.
- Change: `schema.sh new AddLinkedBankAccountAndReworkRedeemRequest` — additive columns for
  gross/fees/net and the widened `LinkedBankAccount` shape; **hand-review carefully, this is a
  rework not a fresh table** (verify no drop/re-add pattern generated for `RedeemRequest`).
- Verify: `schema.sh verify`, migration shown to user, then apply; `RedemptionsEndpointsTests`,
  `LinkedBankAccountsEndpointsTests` (full lifecycle: create -> `wire` webhook -> `Active` ->
  instructions; create redemption -> `payouts` webhook -> `Complete` with gross/fees/net) ->
  `test-full.sh`; `contract.sh`; `check.sh` full solution.

---

## Ticket 08 — Admin cross-tenant views (`.scratch/.../08-admin-cross-tenant-views.md`)

Spec: `docs/features/12-admin-cross-tenant-views.md`. Blocked by: 07.

### 08.1 — Balance-summary column on sub-account list
- Files: `Application/Compliance/GetSubAccount/SubAccountDetailsResult.cs` (add `Money?
  CurrentBalance = null`), `Application/Compliance/ListSubAccounts/ListSubAccountsHandler.cs` (add
  `IFundAccountRepository` dependency, populate via `with` after mapping).
- Verify: `ListSubAccountsHandlerTests` (existing 4 cases unchanged + 2 new: populated/`null`) ->
  `test-fast.sh Application`.

### 08.2 — All-tenants transaction view
- Files: `Application/Ledger/{ListAllTransactionsQuery,ListAllTransactionsQueryHandler}.cs` (thin
  pass-through to `04`'s `ITransactionRepository.ListAllAsync`, `TransactionListFilter` per
  design-pass #4 — record type, not eight positional params).
- Verify: covered under `04`'s handler test suite per spec's own note; confirm no duplicate test
  file created -> `test-fast.sh Application`.

### 08.3 — Admin transactions controller
- Files: `Api/Admin/AdminTransactionsController.cs`, `Program.cs`.
- Change: direct `caller.IsAdmin` gate (no `TenantScopeResolver` — no route segment to arbitrate).
- Verify: `AdminTransactionsControllerTests` (Admin filtered/unfiltered; SubAccount caller
  structural 403) -> `test-full.sh`.

### 08.4 — Master account summary
- Files: `Application/Admin/{GetMasterAccountSummaryQuery,GetMasterAccountSummaryQueryHandler}.cs`,
  `Application/Ledger/Ports/IStablecoinGateway.cs` (add `GetMainWalletBalanceAsync`),
  `Infrastructure/Mocks/MockStablecoinGateway.cs` (returns `MockProviderOptions.
  MainWalletBalanceAmount`), `Infrastructure/Providers/Circle/CircleMintGateway.cs` (real `GET
  /v1/businessAccount/balances`, `walletId` omitted — deliberate exception, per spec §3), `Api/Admin/
  MasterAccountController.cs`, `Program.cs`.
- Verify: `GetMasterAccountSummaryQueryHandlerTests` (sums latest snapshots + main wallet,
  `SubAccountCount`, no-snapshot contributes `0m`) -> `test-fast.sh Application`;
  `MasterAccountControllerTests` (Admin 200, SubAccount structural 403) -> `test-full.sh`;
  `contract.sh`; `check.sh` full solution.

---

## Ticket 09 (+14 stub-bypass, +13 correlation-id) — Notifications outbox

Spec: `docs/features/13-internal-notifications-outbox.md`,
`docs/features/06-audit-and-compliance.md` (§3/§7, correlation-id header). Blocked by: 08.

### 09.1 — Domain
- Files: `Domain/{NotificationDeliveryStatus,NotificationOutboxEntry}.cs`.
- Verify: Domain test (state shape) -> `test-fast.sh Domain`.

### 09.2 — Ports
- Files: `Application/Webhooks/Ports/{INotificationOutboxRepository,INotificationSender}.cs`.
- Verify: `check.sh Application`.

### 09.3 — Dispatcher infrastructure
- Files: `Infrastructure/Notifications/{NotificationDispatcherOptions,HttpNotificationSender,
  NotificationDispatcher,NotificationDispatchBackgroundService}.cs`,
  `Infrastructure/Persistence/NotificationOutboxRepository.cs`, `DbContext` (add `DbSet` +
  composite index `(Status, NextAttemptAtUtc)`).
- Change: bounded exponential backoff, `Math.Min(AttemptCount, 10)` shift-clamp; `AddAsync` does
  not call `SaveChangesAsync` (rides the caller's own commit).
- Verify: `NotificationDispatcherTests` (sender succeeds/fails/no-due-entries-no-save cases) ->
  `test-fast.sh Infrastructure`.

### 09.4 — Same-transaction atomicity test (flagged gap, must add)
- Files: `tests/.../NotificationOutboxAtomicityTests.cs`.
- Change: fault-injection test — poisoned/throwing `SaveChangesAsync` after both writes staged,
  assert **neither** the state change nor the outbox row persisted. Not a positive-path test.
- Verify: `test-full.sh` (Testcontainers).

### 09.5 — Wire the five call sites
- Files: `ProcessExternalEntityDecisionHandler`, `ProcessDepositCommandHandler`,
  `ProcessRecipientDecisionHandler`, `ProcessTransferStatusCommandHandler`,
  `ProcessPayoutStatusCommandHandler` (each: add `INotificationOutboxRepository` dependency,
  `outbox.AddAsync(...)` before the existing `SaveChangesAsync`).
- Change: exactly the five transitions in spec §5's table — resist wiring more.
- Verify: per-handler existing test suites extended with an outbox-row-staged assertion ->
  `test-fast.sh Application`.

### 09.6 — Stub receiver + middleware bypass (closes ticket 14 item 2)
- Files: `Api/Webhooks/InternalNotificationsStubController.cs`,
  `Api/Middleware/CallerIdentityMiddleware.cs` (add `BypassPaths` array,
  `["/internal/notifications"]` first entry), `Program.cs`.
- Verify: `NotificationOutboxDeliveryTests` (all five transitions reach the stub receiver) ->
  `test-full.sh`; `contract.sh` (new stub endpoint).

### 09.7 — Correlation-id response header (ticket 13 item 1)
- Files: `Api/Middleware/` (extend existing exception/response middleware, or add
  `CorrelationIdMiddleware.cs`), `Program.cs`.
- Change: `X-Correlation-Id` set from `HttpContext.TraceIdentifier` on every response, success and
  error alike (ratified 2026-07-17).
- Verify: Api integration test asserting header present on a 200 and a 4xx response ->
  `test-full.sh`; `check.sh` full solution.

---

## Ticket 10 — Demo script E2E (`.scratch/.../10-demo-script-e2e.md`)

Spec: `docs/README.md` §5. Blocked by: 09. Terminal acceptance gate for Phase 1.

### 10.1 — Single end-to-end integration test
- Files: new `tests/TreasuryServiceOrchestrator.IntegrationTests/DemoScriptEndToEndTests.cs`.
- Change: walk PRD §15.1 demo script start to finish against tickets 01-09's **shipped** shapes
  (reconcile signatures at implementation time, not against the original doc draft): admin creates
  sub-account -> screening accepted (+ second rejected/resubmitted) -> deposit address generated
  -> simulated deposit credits ledger/balance -> recipient registered + approved -> transfer
  completes -> redemption completes (gross/fees/net visible) -> tenant isolation (own data only) ->
  admin sees all sub-accounts + master summary -> every step visible in transactions/balance
  history/audit records -> all five notification-worthy transitions reach the stub receiver.
- Verify: `test-full.sh` (Testcontainers, full pipeline) green; `check.sh` clean on the whole
  solution. **No ticket in this plan is "done" until this test is green** — per the ticket's own
  framing, an earlier ticket marked done-but-this-test-fails is not actually done.

---

## Cross-cutting items tracked, not built this phase

- **Ticket 13 items 2-4** (DB-enforced audit immutability, 7-year retention/ops record, PII-at-rest
  confirmation): blocked on real SQL Server provisioning outside LocalDB dev, which does not exist
  yet. Not a task in this plan — re-open when Infrastructure's real DB provisioning is scoped.
- **Ticket 14 item 1** (portal human-user audit header): explicitly deferred — no portal/client
  exists in this repo. Not a task in this plan — re-open when a portal auth client is built.

---

## Execution protocol (per writing-plans skill)

For each numbered sub-task above, in order: dispatch `task-executor` with that task's exact
file(s)/change/verification; then run a two-stage review in the main conversation (spec compliance
against the stated verification step, then code-quality against CLAUDE.md's invariants and
`check.sh`); only then mark it done and move to the next. Migrations always shown to the user
before `schema.sh apply`. This document is updated in place — check off each sub-task with what
proved it (test name + green run) as it completes.
