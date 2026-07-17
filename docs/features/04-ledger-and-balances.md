# Feature: Ledger & Balances (shared foundation)

Source: `docs/PRD.md` §3.1, §9; `docs/Phase_1_Feature_Slices.md` Task 8 ("Ledger — `Transaction` +
`BalanceSnapshot`", PRD §6.2/§6.3/§9), Design-pass correction #2 (ledger-posting module), Task 12
(admin cross-tenant transaction list, read-side only); `docs/adr/0002-fundaccount-vs-wallet.md`;
`docs/adr/0003-transaction-type-mint-folded-into-deposit.md`.

This file owns the **shared** ledger substrate: the `Transaction`, `BalanceSnapshot`, and
`FundAccount` domain entities, the shared ledger-posting module those three entities are mutated
through, and the balance/transaction **read** endpoints. It does not document deposit-specific
funding logic (`ProcessDepositCommandHandler`, the `deposits` webhook topic — see
`09-deposits.md`), transfer-specific debit logic (see `10-transfers.md`), or redemption-specific
debit logic (see `11-redemption.md`); those three feature files each construct their own
`Transaction` rows and call into the posting module described here rather than reimplementing it.
Admin-specific concerns (master-account summary, cross-tenant sub-account balance column) belong
to `12-admin-cross-tenant-views.md` — this file covers only the `ListAllTransactionsQuery` /
`TransactionListFilter` shape those admin views consume.

## 1. Scope / PRD requirement

PRD §9.1: the provider exposes only a **current** balance per wallet plus per-wallet activity
lists — there is no balance-history endpoint at the provider. The service therefore owns a local
ledger:

- Every transaction the service initiates, and every provider webhook event, produces/updates a
  ledger `Transaction` row.
- A `BalanceSnapshot` is recorded per wallet (`FundAccount`, see §2.3) on a schedule and after
  every ledger mutation.
- Provider list endpoints (`deposits`, `transfers`, `payouts` by `walletId`) feed reconciliation
  (PRD §11.4) and backfill — out of scope here, see the reconciliation plan doc.

PRD §9.2 operations, all Admin or the owning SubAccount:

| Operation | Notes |
|---|---|
| Get current balance | Proxied from the local `FundAccount`; short-lived cache permitted. |
| Get balance history | Served from `BalanceSnapshot`s; range + granularity parameters. |
| List transactions | Local ledger; filter by type/status/date range; paginated. |
| Get transaction | Includes provider reference id and status. |

Task 8 supersedes a pre-existing `Deposit` entity + `ProcessDepositCommandHandler` (audit-only
records, no ledger/balance-history model) with this ledger design, and wires the `deposits`
webhook topic into the (already-uniform, per-topic-processor) webhook pipeline. The
deposit-specific parts of that supersession are documented in `09-deposits.md`; this file covers
only what Task 8 introduced that Tasks 9–11 all share.

## 2. Domain design

### 2.1 `TransactionType` / `TransactionStatus` / `DepositSourceType`

```csharp
public enum TransactionType
{
    Deposit,
    Transfer,
    Redemption,
}

public enum TransactionStatus
{
    Pending,
    Complete,
    Failed,
}
```

**No `Mint` value** — ADR 0003 (binding). PRD §9.2's own parenthetical ("filter by type
(deposit/transfer/redemption/mint)") is prose drift, not a contract commitment: PRD §6.2 describes
Circle minting USDC as the *mechanism* by which a fiat-wire deposit settles, not a distinct
ledger event the caller initiates. Both funding paths (fiat wire, on-chain USDC transfer) converge
on the same webhook-driven credit and are recorded as `Deposit`; the two paths are distinguished
instead by `DepositSourceType`:

```csharp
public enum DepositSourceType
{
    Wire,
    OnChain,
}
```

(`Wire`, not `FiatWire` — matches Circle's own literal `source.type: "wire"`, corrected
2026-07-17; any `FiatWire` spelling elsewhere in source drafts is superseded.)

`TransactionStatus` has **no separate `Running` state** — the provider's `transfers`/`payouts`
webhook intermediate event (`running`) maps to `Pending`. This mirrors `TransferStatus` (owned by
`10-transfers.md`) and `PayoutStatus`/redemption status (owned by `11-redemption.md`), which follow
the same three-value convention for consistency across the ledger's callers.

### 2.2 `Transaction` entity

```csharp
public class Transaction
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public Money Amount { get; set; }
    public required string ProviderReferenceId { get; set; }
    public DepositSourceType? DepositSourceType { get; set; }
    public string? FailureReason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

`DepositSourceType` is populated only for `Type == Deposit`; `Transfer`/`Redemption` rows leave it
`null`. `ProviderReferenceId` is unique (`IX_Transactions_ProviderReferenceId`) — it is the natural
idempotency/backfill key: a webhook re-delivery for the same provider event looks the row up by
this id rather than creating a duplicate.

EF mapping (`OnModelCreating`): `ClientCompanyId` uses the shared `ClientCompanyIdCollation`;
`Amount` is a `ComplexProperty` (`amount_value decimal(18,6)`, `currency_code` max length 4) — the
same idiom as `BalanceSnapshot.Balance` and `FundAccount.Balance` below, never a plain `decimal`
column. A composite index `IX_Transactions_SubAccountId_CreatedAtUtc` backs the list/history
queries. No `HasQueryFilter` global tenant filter is applied to `Transaction` — the tenant boundary
is enforced entirely by `TenantScopeResolver` at the handler layer (both the tenant-facing
`TransactionsController` here and `AdminTransactionsController` in `12-admin-cross-tenant-views.md`
rely on this).

### 2.3 `FundAccount` entity — distinct from `Wallet` (ADR 0002)

```csharp
public class FundAccount
{
    public Guid Id { get; set; }
    public required string ClientCompanyId { get; set; }
    public Money Balance { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

ADR 0002 (binding): `FundAccount` is the **local** balance-holding entity, 1:1 with a `Wallet` (the
**provider-side** segregated-wallet record, `walletId`, created by Circle on registration
acceptance — owned by the tenancy/compliance feature file, not this one). Do not collapse the two:
`Wallet` holds provider identity/metadata; `FundAccount` is what the ledger (`Transaction`,
`BalanceSnapshot`) mutates directly, independent of the provider's own non-history-having balance
endpoint. This split is exactly what makes PRD §9.1's "the service therefore owns a local ledger"
possible — collapsing `FundAccount` into `Wallet` would blur the provider-vs-local-ledger
distinction reconciliation depends on.

`FundAccount.Balance` is `Money`, never a raw `decimal` plus a separate `CurrencyCode` field — an
earlier draft of `FundAccount.cs` had exactly that defect (`decimal Balance` + standalone
`string CurrencyCode`), corrected as design-pass correction #1 against the CLAUDE.md invariant that
`Money(decimal Amount, string CurrencyCode)` is the only monetary type crossing the
Domain/Application boundary. Mapped the same way as `Transaction.Amount`
(`ComplexProperty` → `balance_value decimal(18,6)` + `currency_code`).

### 2.4 `BalanceSnapshotReason` / `BalanceSnapshot` entity

```csharp
public enum BalanceSnapshotReason
{
    PostMutation,
    Scheduled,
}

public class BalanceSnapshot
{
    public Guid Id { get; set; }
    public required Guid SubAccountId { get; set; }
    public required string ClientCompanyId { get; set; }
    public Money Balance { get; set; }
    public BalanceSnapshotReason Reason { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
```

A `PostMutation` snapshot is written by the ledger-posting module (§3) every time a `FundAccount`
balance changes; `Scheduled` snapshots are written by a periodic job (out of scope for this file —
owned by whichever ops/scheduling doc covers background jobs) so balance history has regular
points even across quiet periods. `GetHistoryAsync` (§4.2) returns both reasons undifferentiated by
default; callers that need to distinguish them filter client-side on `Reason`.

**Correction — `CapturedAtUtc`/`CreatedAtUtc`/`UpdatedAtUtc` must come from `TimeProvider`.**
Source snippets in `Phase_1_Feature_Slices.md` Task 8 set these fields via `DateTime.UtcNow`
directly throughout (`ProcessDepositCommandHandler.RecordCompleteAsync`, `NewTransaction`, etc.).
That is a defect against CLAUDE.md invariant 2 ("`TimeProvider`, never `DateTime.Now`/
`.UtcNow` directly") — every entity-construction site in the ledger-posting module and its
callers must inject `TimeProvider` and call `timeProvider.GetUtcNow().UtcDateTime`, not
`DateTime.UtcNow`. This file's design in §3 reflects the corrected form; treat any
`DateTime.UtcNow` appearing in the Phase 1 source doc's Task 8/9/10/11 code blocks as
pre-correction and not to be copied verbatim.

## 3. Shared ledger-posting module

### 3.1 Why it exists

Task 8 originally shipped `ProcessDepositCommandHandler` with its own inline "credit `FundAccount`
+ write `Transaction` + write `BalanceSnapshot`" logic and a note that a shared helper was YAGNI
until a second caller appeared. Design-pass correction #2 (2026-07-17) reversed that the moment
Task 9 (transfers) started: deposit credit, transfer debit, and payout/redemption debit each repeat
the same triplet — post a `Transaction`, adjust `FundAccount.Balance`, write a `BalanceSnapshot`.
That triplet is the money-mutation critical path (PRD §14 data integrity) and must have exactly one
implementation, not three drifting copies. `Application/Ledger/LedgerPostingService.cs` is that one
implementation, extracted from `ProcessDepositCommandHandler`'s original inline logic; Task 8's
handler was updated to consume it too, alongside `10-transfers.md`'s
`ProcessTransferStatusCommandHandler` and `11-redemption.md`'s `ProcessPayoutStatusCommandHandler`.

Per design-pass correction #2: **the module's interface stays one method** (post a ledger entry
against a fund account); the transaction/snapshot/fund-account repositories become its internals,
not something each caller wires up separately.

### 3.2 Shape

The source doc states the constraint (one method, extracted from the deposit-credit code, shared by
deposit/transfer/redemption) but does not spell out the exact method signature — the shape below is
this file's synthesis from that constraint plus the `RecordCompleteAsync` logic it was extracted
from, corrected per §2.4's `TimeProvider` note:

```csharp
namespace TreasuryServiceOrchestrator.Application.Ledger;

public sealed record LedgerPosting(
    Guid SubAccountId,
    string ClientCompanyId,
    TransactionType Type,
    TransactionStatus Status,
    Money Amount,                 // signed: positive = credit, negative = debit
    string ProviderReferenceId,
    DepositSourceType? DepositSourceType,
    string? FailureReason,
    string CorrelationId);

public sealed record LedgerPostingResult(Guid TransactionId, Money FundAccountBalance);

public sealed class LedgerPostingService(
    IFundAccountRepository fundAccounts,
    ITransactionRepository transactions,
    IBalanceSnapshotRepository snapshots,
    TimeProvider timeProvider)
{
    public async Task<LedgerPostingResult> PostAsync(
        LedgerPosting posting, CancellationToken cancellationToken = default)
    {
        // 1. Always writes the Transaction row (Complete or Failed — Pending transactions,
        //    e.g. a just-created Transfer awaiting its webhook, are written directly by the
        //    caller via ITransactionRepository.AddAsync, not through this module; this module
        //    is invoked only at the point balance actually moves or a mutation attempt fails).
        // 2. Failed postings (posting.Status == Failed) stop here: no FundAccount mutation,
        //    no BalanceSnapshot. Mirrors ProcessDepositCommandHandler.RecordFailedAsync.
        // 3. Complete postings: find-or-create the FundAccount for ClientCompanyId, apply
        //    posting.Amount (already signed by the caller — credit or debit) to Balance,
        //    stamp UpdatedAtUtc from TimeProvider.
        // 4. Write a BalanceSnapshot(Reason = PostMutation, CapturedAtUtc from TimeProvider)
        //    carrying the resulting balance.
        // 5. Returns the Transaction id and resulting FundAccount balance; callers append
        //    their own audit-log entry (AuditRecord shape is caller-specific: "DepositCredited"
        //    vs. "TransferDebited" vs. "RedemptionDebited"), this module does not audit-log
        //    itself.
    }
}
```

Callers construct their own `Transaction` row shape and pass it in fully formed — the module does
not decide `Type`/`DepositSourceType`/business-specific fields, only the balance-mutation
mechanics. A currency mismatch between `posting.Amount.CurrencyCode` and the existing
`FundAccount.Balance.CurrencyCode` is the caller's responsibility to detect and turn into a
`Status = Failed` posting before calling `PostAsync` (as `ProcessDepositCommandHandler` does today)
— the module itself does not reject mismatched currencies, since by the time a caller reaches
`PostAsync` with `Status = Complete` the decision to credit/debit is already made.

### 3.3 Reserve → transition → complete, two `SaveChangesAsync`

Every caller of this module is itself a mutating handler and therefore follows CLAUDE.md invariant
11: idempotency-check → reserve idempotency key (`SaveChangesAsync #1`, atomic via the
`(ClientCompanyId, IdempotencyKey)` unique index) → gateway/state-transition → persist + audit +
complete idempotency record (`SaveChangesAsync #2`). `LedgerPostingService.PostAsync` itself does
not call `SaveChangesAsync` — it stages `Transaction`/`FundAccount`/`BalanceSnapshot` changes on the
tracked `DbContext` (via the repository `AddAsync` calls / entity mutation), and the caller's
existing `IdempotencyExecutor.ExecuteAsync` wrapper performs the second `SaveChangesAsync` that
persists everything staged inside the work delegate, posting included. This keeps the module a pure
domain-mutation step, not a transaction boundary owner — the callers (`09-deposits.md`,
`10-transfers.md`, `11-redemption.md`) already own that boundary via `IdempotencyExecutor`.

### 3.4 Balance-mutation timing is caller-specific

This module only performs the mutation; *when* each caller invokes it differs and is documented in
the sibling files, not here:

- **Deposits** (`09-deposits.md`): credited immediately on confirmed webhook delivery — a deposit
  has no earlier "pending" state worth recording as a `Transaction` row.
- **Transfers** (`10-transfers.md`): `CreateTransferCommandHandler` only *validates* sufficient
  balance at creation time — it does not call `PostAsync`. The debit happens later, in
  `ProcessTransferStatusCommandHandler`, only once the `transfers` webhook reports `Complete`. A
  `Failed` transfer never touched the balance, so no reversal is needed.
- **Redemption/payouts** (`11-redemption.md`): same pattern as transfers — debit on confirmed
  `Complete`, not at request time.

## 4. Read endpoints

### 4.1 Tenant-scoped: `TransactionsController`, `BalancesController`

Both routed under `api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/...`, both resolve
tenant scope via `(TenantScope.SingleTenant)TenantScopeResolver.Resolve(caller, clientCompanyId)`
(a route with an explicit `clientCompanyId` segment always resolves `SingleTenant`, or
`TenantForbiddenException` → 403 centrally — never a null-forgiving `!`, design-pass correction #3).

| Route | Handler | Query |
|---|---|---|
| `GET .../transactions` | `ListTransactionsQueryHandler` | `ListTransactionsQuery(ResolvedClientCompanyId, TransactionType? Type, TransactionStatus? Status, DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize)` — `page`/`pageSize` default to `1`/`20` when the query string supplies `<= 0`. |
| `GET .../transactions/{transactionId:guid}` | `GetTransactionQueryHandler` | `GetTransactionQuery(ResolvedClientCompanyId, Guid TransactionId)` — 404 (`NotFoundException`) if the transaction doesn't exist *or* belongs to a different `SubAccountId` than the resolved tenant (tenant isolation enforced by identity check, not a filtered query, so a cross-tenant guess reads as "not found," never leaks existence). |
| `GET .../balances` | `GetCurrentBalanceQueryHandler` | `GetCurrentBalanceQuery(ResolvedClientCompanyId)` → `Money`. Returns `Money.Zero("USDC")` when no `FundAccount` row exists yet (no deposits ever credited) — decided 2026-07-17 grilling, see §6. |
| `GET .../balances/history` | `GetBalanceHistoryQueryHandler` | `GetBalanceHistoryQuery(ResolvedClientCompanyId, DateTime FromUtc, DateTime ToUtc)` → `IReadOnlyList<BalanceSnapshot>`, ordered ascending by `CapturedAtUtc`. Both handlers 404 via `NotFoundException` if no `SubAccount` matches `ResolvedClientCompanyId`. |

Both `type`/`status` query parameters bind directly to `TransactionType?`/`TransactionStatus?` —
no `Mint` value is ever a legal filter (ADR 0003).

### 4.2 Repository port shapes backing the above

```csharp
public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByProviderReferenceIdAsync(string providerReferenceId, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> ListAsync(
        Guid subAccountId, TransactionType? type, TransactionStatus? status,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize,
        CancellationToken cancellationToken = default);

    // Admin cross-tenant read — consumed by 12-admin-cross-tenant-views.md's
    // AdminTransactionsController, not by the tenant-scoped controller above.
    Task<IReadOnlyList<Transaction>> ListAllAsync(
        TransactionListFilter filter, CancellationToken cancellationToken = default);
}

public sealed record TransactionListFilter(
    string? ClientCompanyId, TransactionType? Type, TransactionStatus? Status,
    DateTime? FromUtc, DateTime? ToUtc, int Page, int PageSize);

public interface IBalanceSnapshotRepository
{
    Task AddAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<BalanceSnapshot?> GetLatestAsync(Guid subAccountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BalanceSnapshot>> GetHistoryAsync(
        Guid subAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
```

`TransactionListFilter` replaces an earlier eight-positional-parameter `ListAllAsync` signature
(five nullable, two adjacent `DateTime?`) — design-pass correction #4: an unusable interface shape,
replaced with one filter record. `ListAllAsync` is declared on the same `ITransactionRepository`
as the tenant-scoped `ListAsync` (this file); `ListAllTransactionsQuery`/
`ListAllTransactionsQueryHandler` and `AdminTransactionsController` that call it are owned by
`12-admin-cross-tenant-views.md` — this file documents the port shape only, since the
`Transaction`/`ITransactionRepository` types themselves belong here.

```csharp
public sealed record ListAllTransactionsQuery(TransactionListFilter Filter);

public sealed class ListAllTransactionsQueryHandler(ITransactionRepository transactions)
    : IQueryHandler<ListAllTransactionsQuery, IReadOnlyList<Transaction>>
{
    public async Task<IReadOnlyList<Transaction>> HandleAsync(
        ListAllTransactionsQuery query, CancellationToken cancellationToken = default) =>
        await transactions.ListAllAsync(query.Filter, cancellationToken);
}
```

`ListAllAsync`'s EF implementation applies each filter field as an optional `Where` (no filter on
`ClientCompanyId` means every tenant), then pages via `OrderByDescending(CreatedAtUtc).Skip/Take` —
the same pagination idiom as the tenant-scoped `ListAsync`.

## 5. Tests required

| Layer | File | Covers |
|---|---|---|
| Domain | `TransactionTests.cs` | Construction invariants; no `Mint` value exists on `TransactionType` (compile-time proof, not a runtime test — attempting to reference `TransactionType.Mint` must fail to build). |
| Unit | `LedgerPostingServiceTests.cs` | `Complete` posting: creates a `FundAccount` when none exists, applies signed `Amount` to `Balance`, writes one `Transaction` + one `BalanceSnapshot(Reason = PostMutation)`, stamps timestamps from an injected fake `TimeProvider` (not real time). `Failed` posting: writes the `Transaction` only, no `FundAccount` mutation, no `BalanceSnapshot`. Debit (`negative Amount`) reduces `Balance` correctly. |
| Unit | `ListTransactionsQueryHandlerTests.cs` | Lists transactions for the resolved sub-account; throws `NotFoundException` when no sub-account matches `ResolvedClientCompanyId`. |
| Unit | `GetTransactionQueryHandlerTests.cs` | Returns the transaction when it belongs to the resolved tenant; throws `NotFoundException` both when the id doesn't exist and when it belongs to a different `SubAccountId` (cross-tenant access must read identically to not-found). |
| Unit | `GetCurrentBalanceQueryHandlerTests.cs` | Returns `Money.Zero("USDC")` when no `FundAccount` exists yet; returns the real balance otherwise. |
| Unit | `GetBalanceHistoryQueryHandlerTests.cs` | Returns snapshots within `[FromUtc, ToUtc]` ordered ascending; throws `NotFoundException` when no sub-account matches. |
| Unit | `ListAllTransactionsQueryHandlerTests.cs` | Delegates to `ITransactionRepository.ListAllAsync` with the constructed `TransactionListFilter`; no tenant check at this layer (the caller — `AdminTransactionsController`, owned by `12-admin-cross-tenant-views.md` — enforces `caller.IsAdmin` before constructing the query). |
| Integration | `DepositWebhookLedgerTests.cs` (owned end-to-end by `09-deposits.md`, but exercises this file's entities/module) | A `deposits` webhook delivery produces one `Transaction(Type = Deposit, Status = Complete)` and one `BalanceSnapshot(Reason = PostMutation)`, and `GET .../balances` reflects the new total. |
| Integration | `TransactionsAndBalancesEndpointsTests.cs` | Full round trip: seed transactions via the ledger-posting module (or a test-only direct insert), then assert `GET .../transactions`, `GET .../transactions/{id}`, `GET .../balances`, `GET .../balances/history` return the expected shapes and enforce tenant scoping (a different `ClientCompanyId` header gets 403/404, never another tenant's data). |

## 6. Open corrections / decisions log

- **`Mint` transaction type — confirmed absent (ADR 0003).** PRD §9.2's parenthetical listing
  "deposit/transfer/redemption/mint" as filterable types is prose drift against the binding
  three-value `TransactionType` enum; this file documents the enum as shipped/decided, not the PRD
  prose. No further action — ADR 0003 already resolves this authoritatively.
- **`FundAccount` vs `Wallet` — confirmed distinct (ADR 0002).** No discrepancy found; PRD §3.1's
  entity table only names `Wallet`, which is exactly the gap ADR 0002 exists to close (glossary
  update, not a contradiction).
- **`FundAccount.Balance` typed `Money`, not `decimal` + `CurrencyCode` — resolved (design-pass
  correction #1).** An earlier implementation draft had the raw-`decimal` defect; documented here
  as corrected, not as a live discrepancy.
- **`DateTime.UtcNow` vs `TimeProvider` — corrected in this file (see §2.4, §3.2).** The
  `Phase_1_Feature_Slices.md` Task 8 code snippets (`ProcessDepositCommandHandler`,
  `ListTransactionsQueryHandlerTests`, etc.) construct entities via `DateTime.UtcNow` directly
  throughout, which violates CLAUDE.md invariant 2. This file's domain/module design mandates
  `TimeProvider` injection instead; anyone implementing against the Phase 1 source doc's literal
  code blocks must substitute `TimeProvider` at every `DateTime.UtcNow` site touching
  `Transaction`/`BalanceSnapshot`/`FundAccount` timestamps. This is a correction applied here, not
  an unresolved open question.
- **`LedgerPostingService`'s method signature — ratified 2026-07-17 grilling: single
  `PostAsync(LedgerPosting)` with a signed `Money.Amount`, not split `CreditAsync`/`DebitAsync`.**
  Matches the source constraint's literal "stays one method" wording; callers already compute the
  sign, so a split API would diverge from the constraint without solving a concrete problem. No
  longer an open synthesis question — this is the decided shape.
- **`GetCurrentBalanceQueryHandler`'s zero-balance default — ratified 2026-07-17 grilling:
  `Money.Zero("USDC")`, not `Money.Zero("USD")`.** Every real `FundAccount` this stablecoin-only
  product ever creates is USDC-denominated; a `USD` zero-balance followed by a `USDC` real balance
  was a currency inconsistency across the no-activity→first-deposit transition. Corrected from the
  Phase 1 source snippet's literal `"USD"` default.
- No other discrepancies found between PRD §9, PRD §3.1, `Phase_1_Feature_Slices.md` Task 8/Task
  12's ledger-read portion, ADR 0002, and ADR 0003 during this pass.
