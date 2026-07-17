# 08 — Banking & Wire Instructions

Source: `docs/PRD.md` §5 (Appendix A items 3/10/11, Appendix B rows for `banks/wires`),
`docs/Phase_1_Feature_Slices.md` Task 11 (`LinkedBankAccount`-entity and `wire`-topic portions
only — lines ~7089-7945), `docs/Phase_3_Circle_Integration_Plan.md` Task 3 (`CreateLinkedBankAccountAsync`
row and the wire-instructions bullet). Module: `Ledger` per `docs/adr/0001-module-boundaries.md`.

**Not owned here** — `RedeemRequest`, `CreateRedemptionCommand`, `ProcessPayoutStatusCommand`, and
the `payouts` webhook topic (redemption's own async lifecycle, which *consumes* a `LinkedBankAccount`
as its destination) belong to the sibling redemption/payouts feature file
(`.scratch/treasury-service-orchestrator/issues/07-redemption-and-linked-bank-account.md` covers
both under one ticket, but this file's scope is strictly `LinkedBankAccount` creation/read, wire
instructions, and the `wire` webhook topic — not what redemption does with a `LinkedBankAccount`
once it exists). Terminology (`ClientCompanyId`, tenant scope, Admin/SubAccount roles) is canonical
in `01-tenancy-and-authorization.md`. The generic webhook pipeline (durable inbox, dedup,
per-topic dispatch, SNS transport) is canonical in `03-webhook-processing.md` — this file only adds
the `wire`-topic-specific processor. Mock-mode mechanics (`IMockWebhookScheduler`, the ADR 0007
payload-shape contract) are canonical in `02-mock-mode.md`.

---

## 1. Scope / PRD requirement

Source: PRD §5 (covers original requirement items 3, 10, 11).

| Operation | Access | Notes |
|---|---|---|
| Create (link) wire bank account | Admin | Distributor-level; must complete provider verification before use as a redemption destination. |
| Get / list linked bank accounts | Admin | Includes verification status. |
| Get Distributor wire instructions | Admin | Instructions for funding the Distributor's primary (Master Account) wallet. |
| Get entity-scoped wire instructions | Admin, owning SubAccount | Instructions carrying the sub-account's entity-scoped `trackingRef`; wires quoting it are credited to the entity's wallet. |

Ground truth (PRD §5.1, restated because it drives every design choice below):

- Wire instructions are **generated, read-only artifacts** — nothing to update or delete
  (Appendix A items 3 and 11: "Read-only generated artifact; nothing to create/update/delete").
- `LinkedBankAccount` create/read only; removal, if ever required, is an operational action at the
  provider, not a product endpoint (Appendix A item 10).
- The entity-scoped `trackingRef` is the routing mechanism for institutional funding: the end
  client wires fiat quoting the `trackingRef`, and Circle credits the entity wallet — this is the
  fiat-wire half of the deposit-crediting workflow owned by `09-deposits.md` (§6.2 of the PRD); this
  file produces the `trackingRef`, `09-deposits.md` consumes the resulting `deposits` webhook.
- An optional `customerExternalRef` (format `EXT` + 18 alphanumerics) may be included in the bank
  memo for reconciliation — Circle echoes it back on the `deposits` webhook (confirmed live, §6
  below) when present, but the product does not require a caller to supply one to use wire
  instructions.
- **Verification is asynchronous.** A `LinkedBankAccount` is created `Pending` and only becomes
  usable as a redemption destination once the `wire` webhook topic reports `complete` (→ `Active`).
  There is no synchronous verification-at-create path — PRD correction (added 2026-07-17, see
  `.claude/CLAUDE.md` project history and `07-redemption-and-linked-bank-account.md`'s corrections
  header #6).
- `LinkedBankAccount` is a **Distributor-level** entity — it carries no `ClientCompanyId` and is
  not tenant-scoped like every other persisted entity in this system (PRD entity table, line 141;
  `CONTEXT.md` "shared banking resource," confirmed by `Phase_1_Feature_Slices.md` line 7197: "carries
  no `ClientCompanyId`... unlike every other entity in this system").

---

## 2. Domain design

### 2.1 `LinkedBankAccountStatus`

```csharp
namespace TreasuryServiceOrchestrator.Domain;

public enum LinkedBankAccountStatus
{
    Pending,
    Active,
    Failed,
}
```

Three values, mirroring the `wire` webhook's own three statuses (`pending`/`complete`/`failed`) —
`Active` (not `Complete`) is the domain name for Circle's `complete`, chosen because "Active" is
what the entity's *usability* means to the rest of the system (only an `Active` `LinkedBankAccount`
is a legal redemption destination), not because it echoes Circle's wire.

### 2.2 `LinkedBankAccount` entity

```csharp
namespace TreasuryServiceOrchestrator.Domain;

public class LinkedBankAccount
{
    public Guid Id { get; set; }
    public required string BeneficiaryName { get; set; }
    public required string AccountNumber { get; set; }
    public required string RoutingNumber { get; set; }
    public required string BankName { get; set; }
    public required string CircleBankAccountId { get; set; }
    public LinkedBankAccountStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

No `ClientCompanyId` field (§1). Timestamps via `TimeProvider.GetUtcNow().UtcDateTime`
(CLAUDE.md invariant 2) — same correction `04-ledger-and-balances.md` §2.4 already applies to every
other entity-construction site; the `Phase_1_Feature_Slices.md` Task 11 code blocks construct
`LinkedBankAccount`/`RedeemRequest` timestamps via `DateTime.UtcNow` directly and must not be copied
verbatim.

**This four-flat-field shape is a deliberate simplification, not Circle's real request/response
schema — see §5's discrepancy.** It is sufficient for what the domain and Application tiers need
to reason about (identity, verification state); the real Circle field layout is nested and is
mapped at the Infrastructure boundary only (§6).

---

## 3. Application design

Module: `Application/Ledger/LinkedBankAccounts/` (ADR 0001 — **not** a flat top-level
`Application/LinkedBankAccounts/`; that flat form appeared in an earlier draft of this session's
work and was corrected before any code shipped).

### 3.1 `ILinkedBankAccountRepository` port

`Application/Ledger/Ports/ILinkedBankAccountRepository.cs`:

```csharp
public interface ILinkedBankAccountRepository
{
    Task AddAsync(LinkedBankAccount account, CancellationToken cancellationToken);

    Task<LinkedBankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<LinkedBankAccount?> GetByCircleBankAccountIdAsync(
        string circleBankAccountId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkedBankAccount>> ListAsync(CancellationToken cancellationToken);
}
```

No tenant-id parameter anywhere on this port — unlike every other repository in this system, there
is no tenant to scope by (§1). This is the one port in the codebase that legitimately does **not**
follow `01-tenancy-and-authorization.md` §2.6 mechanism 1 (key-scoped queries), because there is no
`ClientCompanyId` to key by; it also isn't mechanism 2 (Admin/`AllTenants` list-style port with a
handler-level `IsAdmin` re-check) in the tenancy sense — but the *access-control* re-check still
applies for a different reason: every operation on this entity is Admin-only regardless of scope
shape (§3.3), so the handler-level `caller.IsAdmin` check plays the same defense-in-depth role
mechanism 2's audit does, just without a `TenantScope` involved at all.

### 3.2 `IStablecoinGateway` additions

Two gateway methods live on `IStablecoinGateway` (`Application/Ledger/Ports/IStablecoinGateway.cs`)
per the Compliance/Ledger gateway split (`02-mock-mode.md` §2, ADR 0006) — bank accounts and wire
instructions are money-moving-adjacent Ledger concerns, not `ISubAccountGateway`'s entity/
registration surface:

```csharp
public interface IStablecoinGateway
{
    // ... existing methods (RedeemAsync, CreateTransferAsync, GenerateDepositAddressAsync,
    // RegisterRecipientAsync, GetTransferStatusAsync, ListRecentDepositsAsync — unchanged) ...

    Task<CreateLinkedBankAccountGatewayResult> CreateLinkedBankAccountAsync(
        CreateLinkedBankAccountGatewayRequest request, CancellationToken cancellationToken);

    Task<WireInstructionsGatewayResult> GetWireInstructionsAsync(
        GetWireInstructionsGatewayRequest request, CancellationToken cancellationToken);
}

public sealed record CreateLinkedBankAccountGatewayRequest(
    string IdempotencyKey, string BeneficiaryName, string AccountNumber,
    string RoutingNumber, string BankName);

public sealed record CreateLinkedBankAccountGatewayResult(string CircleBankAccountId, string Status);

public sealed record GetWireInstructionsGatewayRequest(
    string CircleBankAccountId, string Currency, string? WalletId);

public sealed record WireInstructionsGatewayResult(
    string TrackingRef, string BeneficiaryName, string BeneficiaryAddress,
    string BankName, string SwiftCode, string RoutingNumber, string MaskedAccountNumber,
    string Currency);
```

**`GetWireInstructionsAsync` is this file's own addition, not a verbatim source quote.**
`Phase_1_Feature_Slices.md` Task 11 never added a wire-instructions method to any gateway port —
its interface listing (§3 above / line 7259-7282 of the source doc) has no such method.
`Phase_3_Circle_Integration_Plan.md` Task 3 flags the exact same gap explicitly: *"confirm which
port (`ISubAccountGateway` vs `IStablecoinGateway`) Phase 1 actually placed them on before adding
(`Grep` both interfaces for `WireInstructions` first)"* — a `Grep` of the shipped
`src/` tree for `WireInstructions`/`GetWireInstructions` during this write-up returned **no
matches on either port**. This file resolves that open question rather than leaving it for Phase 3
Task 3 to rediscover: `IStablecoinGateway` is the correct home (money-moving-adjacent, same module
as `CreateLinkedBankAccountAsync`, same Circle `businessAccount` API surface), and the DTO shapes
above are proposed here so Phase 3 Task 3 has a concrete signature to implement against instead of
inventing one mid-task. `CurrencyCode` and `WalletId` are both required Circle-side facts (§5), not
optional conveniences — the request DTO reflects that (`Currency` non-nullable, `WalletId` nullable
for the Distributor case).

`IdempotencyKey` is added to `CreateLinkedBankAccountGatewayRequest` here — Phase 1's version of
this record (`Phase_1_Feature_Slices.md` line 7251-7252) omits it, which is itself a defect against
CLAUDE.md invariant 11 ("idempotency key required on every mutating consumer operation and
forwarded to the provider on money-moving calls") and against Phase 3 Task 4's own idempotency-audit
scope, which explicitly lists `CreateLinkedBankAccountAsync` as one of the five methods that must
forward the caller's reserved key, not a freshly generated one. Carried as corrected here.

### 3.3 `CreateLinkedBankAccountCommand` / handler

`Application/Ledger/LinkedBankAccounts/CreateLinkedBankAccountCommand.cs`:

```csharp
public sealed record CreateLinkedBankAccountCommand(
    string BeneficiaryName, string AccountNumber, string RoutingNumber, string BankName,
    string IdempotencyKey, string CorrelationId);

public sealed record CreateLinkedBankAccountResult(
    Guid LinkedBankAccountId, string CircleBankAccountId, LinkedBankAccountStatus Status);
```

`CreateLinkedBankAccountCommandValidator` — `NotEmpty()` on all four bank-detail fields plus
`IdempotencyKey`/`CorrelationId`.

`CreateLinkedBankAccountCommandHandler(IStablecoinGateway gateway, ILinkedBankAccountRepository
linkedBankAccounts, IAuditLogService auditLog, IUnitOfWork unitOfWork)`:

1. Validate.
2. Call `gateway.CreateLinkedBankAccountAsync(...)` — Circle always returns `pending` here (§5);
   this is not a decision point, it is the async-verification contract.
3. Construct `LinkedBankAccount { Status = LinkedBankAccountStatus.Pending, ... }`, `AddAsync`.
4. Audit-log `"LinkedBankAccountCreated"`.
5. `unitOfWork.SaveChangesAsync`.

**No `IdempotencyExecutor`/two-`SaveChangesAsync` pattern here** — `IdempotencyExecutor` keys on
`(ClientCompanyId, IdempotencyKey)`, and `LinkedBankAccount` has no `ClientCompanyId` (§3.1). A
single direct `SaveChangesAsync` is used instead, matching `Phase_1_Feature_Slices.md`'s own note
at line 7628-7629. This is a narrow, deliberate exception to CLAUDE.md invariant 11's "two
`SaveChangesAsync` calls" clause, not a violation of it — the invariant's purpose (atomic
idempotency-key reservation before the gateway call) does not apply to an entity that structurally
cannot have a `(ClientCompanyId, IdempotencyKey)` row. The `IdempotencyKey` passed to the command is
still forwarded to Circle's `idempotencyKey` body field (§3.2, §6) — invariant 11's *forwarding*
clause still applies even though the *two-phase reservation* clause does not.

### 3.4 `ListLinkedBankAccountsQuery` / `GetLinkedBankAccountQuery`

```csharp
public sealed record ListLinkedBankAccountsQuery;

public sealed class ListLinkedBankAccountsQueryHandler(ILinkedBankAccountRepository linkedBankAccounts)
    : IQueryHandler<ListLinkedBankAccountsQuery, IReadOnlyList<LinkedBankAccount>>
{
    public async Task<IReadOnlyList<LinkedBankAccount>> HandleAsync(
        ListLinkedBankAccountsQuery query, CancellationToken cancellationToken = default) =>
        await linkedBankAccounts.ListAsync(cancellationToken);
}

public sealed record GetLinkedBankAccountQuery(Guid LinkedBankAccountId);

public sealed class GetLinkedBankAccountQueryHandler(ILinkedBankAccountRepository linkedBankAccounts)
    : IQueryHandler<GetLinkedBankAccountQuery, LinkedBankAccount>
{
    public async Task<LinkedBankAccount> HandleAsync(
        GetLinkedBankAccountQuery query, CancellationToken cancellationToken = default) =>
        await linkedBankAccounts.GetByIdAsync(query.LinkedBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Linked bank account '{query.LinkedBankAccountId}' not found.");
}
```

Both handlers return the `LinkedBankAccount` **domain entity** directly — this matches shipped
Phase 1 code (`Phase_1_Feature_Slices.md` line 7679-7703) and the equivalent pattern in
`04-ledger-and-balances.md` §4.1 (`GetTransactionQueryHandler` also returns a domain entity through
`IQueryHandler<..., Transaction>`). It is flagged, not silently changed, in §8 as a standing tension
with CLAUDE.md invariant 5 ("Domain entities never leak past the Application boundary into an API
response").

### 3.5 `GetWireInstructionsQuery` (Distributor and entity-scoped)

One query handles both PRD §5.1 rows ("Get Distributor wire instructions" and "Get entity-scoped
wire instructions") — they are the same Circle call with an optional `walletId`:

```csharp
public sealed record GetWireInstructionsQuery(Guid LinkedBankAccountId, string Currency, string? WalletId);

public sealed record WireInstructionsResult(
    string TrackingRef, string BeneficiaryName, string BeneficiaryAddress,
    string BankName, string SwiftCode, string RoutingNumber, string MaskedAccountNumber,
    string Currency);

public sealed class GetWireInstructionsQueryHandler(
    ILinkedBankAccountRepository linkedBankAccounts, IStablecoinGateway gateway)
    : IQueryHandler<GetWireInstructionsQuery, WireInstructionsResult>
{
    public async Task<WireInstructionsResult> HandleAsync(
        GetWireInstructionsQuery query, CancellationToken cancellationToken = default)
    {
        var account = await linkedBankAccounts.GetByIdAsync(query.LinkedBankAccountId, cancellationToken)
            ?? throw new NotFoundException($"Linked bank account '{query.LinkedBankAccountId}' not found.");

        var instructions = await gateway.GetWireInstructionsAsync(
            new GetWireInstructionsGatewayRequest(account.CircleBankAccountId, query.Currency, query.WalletId),
            cancellationToken);

        return new WireInstructionsResult(
            instructions.TrackingRef, instructions.BeneficiaryName, instructions.BeneficiaryAddress,
            instructions.BankName, instructions.SwiftCode, instructions.RoutingNumber,
            instructions.MaskedAccountNumber, instructions.Currency);
    }
}
```

`WalletId = null` produces Distributor (Master Account) instructions; a non-null `WalletId` (the
target sub-account's provider `walletId`, resolved the same way `09-deposits.md`'s deposit-address
generation resolves it) produces entity-scoped instructions carrying that entity's own
`trackingRef`. This mirrors the deposit-address generation pattern in PRD §6.3 ("omitting `walletId`
targets the Master Account wallet" — the same query-param-presence semantics, confirmed independently
for wire instructions in §5 below).

This query/handler is **this file's own design**, not transcribed from either source task doc, for
the same reason as §3.2's gateway addition: no wire-instructions read path exists anywhere in
`Phase_1_Feature_Slices.md`'s Task 11, and `Phase_3_Circle_Integration_Plan.md` Task 3 only says to
"implement as part of this task" without specifying command/query/controller shapes. Flagged again
in §8.

### 3.6 `ProcessLinkedBankAccountStatusCommand` — the `wire`-webhook-driven state transition

```csharp
public sealed record ProcessLinkedBankAccountStatusCommand(string CircleBankAccountId, string Status);

public sealed record ProcessLinkedBankAccountStatusResult(Guid LinkedBankAccountId, LinkedBankAccountStatus Status);

internal static class LinkedBankAccountStatusMapper
{
    public static LinkedBankAccountStatus Map(string circleStatus) => circleStatus.ToLowerInvariant() switch
    {
        "pending" => LinkedBankAccountStatus.Pending,
        "complete" => LinkedBankAccountStatus.Active,
        "failed" => LinkedBankAccountStatus.Failed,
        _ => throw new InvalidOperationException($"Unrecognized linked bank account status '{circleStatus}'."),
    };
}

public sealed class ProcessLinkedBankAccountStatusCommandHandler(
    ILinkedBankAccountRepository linkedBankAccounts, IAuditLogService auditLog,
    IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult>
{
    public async Task<ProcessLinkedBankAccountStatusResult> HandleAsync(
        ProcessLinkedBankAccountStatusCommand command, CancellationToken cancellationToken = default)
    {
        var account = await linkedBankAccounts.GetByCircleBankAccountIdAsync(
            command.CircleBankAccountId, cancellationToken)
            ?? throw new NotFoundException(
                $"Linked bank account with Circle id '{command.CircleBankAccountId}' not found.");

        var newStatus = LinkedBankAccountStatusMapper.Map(command.Status);

        if (account.Status == newStatus)
        {
            // Idempotent: SNS redelivery of the same terminal event must not double-audit
            // or re-stamp UpdatedAtUtc. Mirrors ProcessTransferStatusCommandHandler (10-transfers.md).
            return new ProcessLinkedBankAccountStatusResult(account.Id, account.Status);
        }

        account.Status = newStatus;
        account.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await auditLog.AppendAsync(
            "LinkedBankAccountStatusChanged", "LinkedBankAccount", account.Id.ToString(),
            /* { previousStatus, newStatus } */ default!, "ADMIN", command.CircleBankAccountId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProcessLinkedBankAccountStatusResult(account.Id, account.Status);
    }
}
```

No `LedgerPostingService` call here — unlike `deposits`/`payouts`/`transfers`, a `wire` event never
moves money; it only flips a verification flag. `04-ledger-and-balances.md`'s posting module is
therefore correctly **not** a dependency of this handler.

---

## 4. Api design

`Api/Ledger/LinkedBankAccountsController.cs`, flat route (`api/v{version:apiVersion}/linked-bank-accounts`,
no `{clientCompanyId}` segment) — matching the entity's Distributor-level, non-tenant-scoped nature
(`Phase_1_Feature_Slices.md` line 8851). Every action starts with a hand-rolled `if (!caller.IsAdmin)
throw new TenantForbiddenException(...)` check, since this controller has no `TenantScopeResolver`
call to lean on (there is no tenant to resolve) — this is the controller-level equivalent of §3.1's
"defense-in-depth without a `TenantScope`" note.

| Route | Access | Handler |
|---|---|---|
| `POST /linked-bank-accounts` | Admin | `CreateLinkedBankAccountCommand` |
| `GET /linked-bank-accounts` | Admin | `ListLinkedBankAccountsQuery` |
| `GET /linked-bank-accounts/{id:guid}` | Admin | `GetLinkedBankAccountQuery` |
| `GET /linked-bank-accounts/{id:guid}/instructions?currency=&walletId=` | Admin (any `walletId`, including omitted); owning SubAccount (only its own resolved `walletId`) | `GetWireInstructionsQuery` |

The instructions route's access rule is the one place this file's Distributor-level entity design
intersects tenant scoping: PRD §5.1 grants entity-scoped instructions to "Admin, owning SubAccount,"
which means the controller **does** need a tenant check here, unlike the other three routes — a
SubAccount caller may request instructions only for its own wallet. Concretely: resolve the
caller's own sub-account `walletId` via the same lookup `09-deposits.md`'s deposit-address endpoint
uses; a SubAccount caller passing no `walletId` defaults to its own; a SubAccount caller passing a
`walletId` that resolves to a different tenant gets `TenantForbiddenException` → 403
`tenant-forbidden`; a SubAccount caller may never omit `walletId` in a way that resolves to the
Distributor instructions (that is Admin-only, per the "Admin" row). This access rule is this file's
own derivation from PRD §5.1's operations table (§1) — neither Phase 1 nor Phase 3 defines the
controller action at all (§3.5).

---

## 5. Circle provider mapping (verified live)

| Product operation | Circle endpoint | Verified detail |
|---|---|---|
| Create wire bank account | `POST /v1/businessAccount/banks/wires` | Body is **one of three schemas** depending on bank location/IBAN support (verified live 2026-07-17 against `https://developers.circle.com/api-reference/circle-mint/account/create-business-wire-account`): US banks (`WireCreationRequestUs`: `accountNumber`, `routingNumber` [ABA], `billingDetails{name,city,country,line1,postalCode,line2?,district?}`, `bankAddress{country,bankName?,city?,line1?,line2?,district?}`); IBAN non-US (`WireCreationRequestIban`: `iban`, `billingDetails`, `bankAddress{city,country}`); non-IBAN non-US (`WireCreationRequestAccountNumber`: `accountNumber`, `routingNumber` [SWIFT/BIC], `billingDetails`, `bankAddress{bankName,city,country}`). `idempotencyKey` (UUID v4) is a root-level body field on all three. Response includes `id`, `type:"wire"`, `status`, `description`, **`trackingRef`**, `transferTypesInfo`, `policyEvaluation`, `fingerprint`, `billingDetails`, `bankAddress`, `createDate`, `updateDate`, `virtualAccountEnabled`. |
| List wire bank accounts | `GET /v1/businessAccount/banks/wires` | No query parameters; returns an array of the same response shape as create. |
| Get wire bank account | `GET /v1/businessAccount/banks/wires/{id}` | Path `id`; single object. |
| Distributor / entity wire instructions | `GET /v1/businessAccount/banks/wires/{id}/instructions` | Query params: `currency` (**required** — not documented in PRD §5.2/Appendix B, discrepancy noted below), `walletId` (**optional**; verified live 2026-07-17: "if not provided, the instructions will be for your default [Master Account] wallet" — same omitted-means-Distributor semantics as deposit-address generation, PRD §6.3). Response includes `trackingRef`, `beneficiary{name,address}`, `beneficiaryBank{name,swiftCode,routingNumber,accountNumber (masked),currency,address}`. |
| Bank-account verification events | `wire` webhook topic | Confirmed as a **named, documented topic** (not merely inferable), verified live 2026-07-17 against `https://developers.circle.com/circle-mint/references/webhook-notifications` and cross-checked in the local mirror `docs/circle-mint-docs/reference/webhook-notifications.md`: `pending` ("Circle is reviewing the bank account"), `complete` ("linked and can be used for deposits and payouts"), `failed` ("could not be linked"). Example payload: `{"wire":{"id","status","description","trackingRef","fingerprint","billingDetails":{...},"createDate","updateDate"}}`. |

### 5.1 `customerExternalRef` cross-check (informational, owned end-to-end by `09-deposits.md`)

Confirmed live: the `deposits` webhook echoes `customerExternalRef` back on the deposit resource
when the originating wire memo included one matching the `EXT...` format — "so you can reconcile
the credit to the originating client." This file only notes it produces the wire instructions the
end client's memo quotes; consuming the echoed field on the `deposits` payload is `09-deposits.md`'s
responsibility, not repeated here.

---

## 6. Mock-mode behavior

See `02-mock-mode.md` for the full mock-provider design (`MockProviderOptions`, `MockModeGuard`,
`IMockWebhookScheduler`, ADR 0007 payload-shape contract). This file's slice of it:

`MockStablecoinGateway.CreateLinkedBankAccountAsync`:

1. Standard latency/failure-injection preamble (`02-mock-mode.md` §3.4).
2. Generates `bank-account-{guid}`, returns `CreateLinkedBankAccountGatewayResult(id, "pending")`
   immediately — the mock preserves async-verification semantics rather than shortcutting them
   (`Phase_1_Feature_Slices.md` line 7498-7499: "Bank-account verification is asynchronous...not
   completed synchronously").
3. Schedules a `"wire"` webhook via `IMockWebhookScheduler` with the real Circle envelope shape
   (ADR 0007): `{"wire":{"id":"bank-account-...","status":"complete"}}` — deterministic `complete`
   in the mock (no injected-failure path modeled for this topic in Phase 1's test suite; a future
   pass could add a magic-suffix `BankName` the same way `MockSubAccountGateway` uses a magic
   business-name suffix for `Rejected`, but Phase 1 does not do this).

`MockStablecoinGateway.GetWireInstructionsAsync` (this file's addition, since the method itself is
this file's addition per §3.2): returns a deterministic synthetic `WireInstructionsGatewayResult`
derived from the `CircleBankAccountId` — e.g. `TrackingRef = $"MOCK{circleBankAccountId[..10].ToUpperInvariant()}"`
— so integration tests exercising the full create → verify → get-instructions flow get a stable,
assertable value without a real Circle sandbox call. No webhook is scheduled by this method (reads
never produce webhooks).

The `wire` topic dispatches through the **same real pipeline** every other mock-emitted topic uses
(`MockWebhookDispatcher.DispatchOneAsync` → `WebhookProcessor.HandleAsync` → `WireWebhookTopicProcessor`,
§7) — mock mode is a producer feeding the real pipeline, not a shortcut around it
(`02-mock-mode.md` §3.5).

---

## 7. Real Circle HTTP integration

`Infrastructure/Providers/Circle/CircleMintGateway.cs` (Phase 3 Task 3), against the named
`HttpClient` ("Circle") registered via `IHttpClientFactory` (Phase 3 Task 1 — canonical elsewhere,
not restated here).

`CreateLinkedBankAccountAsync`:

- `POST /v1/businessAccount/banks/wires`.
- Body: `idempotencyKey` = the **caller's reserved key** (§3.2's correction — not a
  gateway-generated GUID; Phase 3 Task 4's idempotency-forwarding audit explicitly names this
  method), plus the applicable one of the three schemas in §5. **Only the US schema
  (`accountNumber`/`routingNumber`/`billingDetails`/`bankAddress`) is in scope for Phase 1's
  four-flat-field `LinkedBankAccount` domain shape** (§2.2) — mapping `BeneficiaryName` →
  `billingDetails.name`, `BankName` → `bankAddress.bankName`, `AccountNumber`/`RoutingNumber` →
  root-level fields, with `billingDetails.city`/`country`/`line1`/`postalCode` and
  `bankAddress.country` left as **open fields the domain entity does not currently carry** — see
  §8's discrepancy; the gateway cannot build a well-formed US wire-creation request from the
  Phase 1 domain shape alone as it stands today without either a config-level default billing
  address or an entity-shape change.
- Response mapping: `CircleBankAccountId = response.Id`, `Status = response.Status`
  (`"pending"` expected on every create — Circle never returns a terminal status synchronously,
  per §5's `wire` topic confirmation).

`GetWireInstructionsAsync`:

- `GET /v1/businessAccount/banks/wires/{circleBankAccountId}/instructions?currency={currency}` plus
  `&walletId={walletId}` **only when `request.WalletId` is non-null** — omitting the query
  parameter entirely (not passing an empty string) is what produces Distributor instructions (§5).
- Response mapping straight through to `WireInstructionsGatewayResult` per the field list in §5.

`ListRecentDepositsAsync`/other `IStablecoinGateway` methods are unchanged by this file — see
`09-deposits.md`, `10-transfers.md`, `11-redemption.md` for their own provider-mapping sections.

**`source`/idempotency hazards that also apply to this file's calls**: Phase 3 Task 3's
Global Constraint that Circle defaults an omitted `source`/wallet-targeting field to the
Distributor's Master Account (verified 2026-07-17, PRD §6.3/Appendix B) is the same hazard family
as `walletId` on the instructions call — the gateway must never *accidentally* omit `walletId` when
an entity-scoped result was intended; §4's controller-level access rule and §3.5's query design are
what keep "entity-scoped vs. Distributor" an explicit caller choice rather than an ambient default.

---

## 8. `wire` webhook topic processor

Plugs into the generic pipeline in `03-webhook-processing.md` §2.2 (`IWebhookTopicProcessor`); this
section covers only the topic-specific parsing and dispatch.

`Infrastructure/Webhooks/WireWebhookTopicProcessor.cs`:

```csharp
public sealed class WireWebhookTopicProcessor(
    ICommandHandler<ProcessLinkedBankAccountStatusCommand, ProcessLinkedBankAccountStatusResult> decisionHandler)
    : IWebhookTopicProcessor
{
    public string Topic => "wire";

    public async Task ProcessAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<WirePayload>(payloadJson)
            ?? throw new InvalidOperationException("wire webhook payload deserialized to null.");

        if (payload.Wire?.Id is null || payload.Wire.Status is null)
        {
            throw new InvalidOperationException("wire webhook payload missing bank account id or status.");
        }

        await decisionHandler.HandleAsync(
            new ProcessLinkedBankAccountStatusCommand(payload.Wire.Id, payload.Wire.Status), cancellationToken);
    }

    private sealed record WirePayload
    {
        [JsonPropertyName("wire")]
        public WireResourcePayload? Wire { get; init; }
    }

    private sealed record WireResourcePayload
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
```

A thrown `InvalidOperationException` (missing `id`/`status`) propagates to `WebhookProcessor`,
which marks the inbox entry `Failed` and returns HTTP 5xx for SNS redelivery
(`03-webhook-processing.md` §2.4) — correct here, since a malformed `wire` payload from a real
Circle delivery is a transient/parsing problem worth retrying, not an unhandled-topic case.

Registered in DI alongside every other topic processor: `builder.Services.AddScoped<IWebhookTopicProcessor,
WireWebhookTopicProcessor>();` — `Phase_1_Feature_Slices.md` line 8716 confirms this registration
site.

Idempotency on redelivery is handled two ways: the pipeline's own dedup (unique `CircleEventId` —
`03-webhook-processing.md` §2.3) stops an exact SNS redelivery before it reaches this processor at
all; §3.6's handler-level `account.Status == newStatus` short-circuit additionally covers the case
of two *distinct* Circle events reporting the same terminal status (e.g. a legitimate Circle-side
retry with a new `MessageId`), which the pipeline dedup alone would not catch.

---

## 9. Tests required

Per the testing strategy in `.claude/CLAUDE.md`:

**Domain** — n/a; `LinkedBankAccountStatus` is a plain enum, no invariant beyond the three-value
`switch` in `LinkedBankAccountStatusMapper` (covered as an Application-tier unit, not a Domain
test).

**Application** (xUnit v3, Moq/NSubstitute, FluentAssertions):

| File | Covers |
|---|---|
| `CreateLinkedBankAccountCommandHandlerTests.cs` | Calls gateway, persists `Pending`, audits, single `SaveChangesAsync` (no `IdempotencyExecutor` — §3.3). Forwards `command.IdempotencyKey` verbatim into `CreateLinkedBankAccountGatewayRequest.IdempotencyKey`, not a freshly generated value. |
| `ProcessLinkedBankAccountStatusCommandHandlerTests.cs` | `complete` → `Active`, audits, saves. `failed` → `Failed`. Already-matching status → no-op, no audit call, no `SaveChangesAsync` (idempotent replay/redelivery). Unknown `CircleBankAccountId` → `NotFoundException`. Unrecognized status string → `InvalidOperationException` from the mapper. |
| `GetWireInstructionsQueryHandlerTests.cs` | Resolves the `LinkedBankAccount` by id, calls gateway with `(CircleBankAccountId, Currency, WalletId)`, maps result through. Unknown `LinkedBankAccountId` → `NotFoundException` (gateway never called). |
| `ListLinkedBankAccountsQueryHandlerTests.cs` / `GetLinkedBankAccountQueryHandlerTests.cs` | Straightforward pass-through / 404 cases, mirroring `04-ledger-and-balances.md`'s equivalent handler tests. |
| `WireWebhookTopicProcessorTests.cs` | `Topic == "wire"`. Well-formed payload → `ProcessLinkedBankAccountStatusCommand(id, status)` invoked exactly once with the parsed values. Payload missing `id`/`status` → `InvalidOperationException`, handler never invoked. |
| `MockStablecoinGatewayLinkedBankAccountTests.cs` | `CreateLinkedBankAccountAsync` returns `pending` + schedules exactly one `"wire"` webhook with `status:"complete"` and the returned `CircleBankAccountId` embedded in the payload JSON. `FailureInjectionRate = 1.0` → `ProviderUnavailableException`, no webhook scheduled. `GetWireInstructionsAsync` returns a deterministic `TrackingRef` derived from the input id, no webhook scheduled. |

**Infrastructure** (xUnit v3, fixture-first per Phase 3's testing approach, `CircleMintGatewayTests.cs`):

- `CreateLinkedBankAccountAsync` builds the US wire-creation body shape (§7) with `idempotencyKey`
  equal to the value passed into the request DTO (Phase 3 Task 4's idempotency-forwarding assertion,
  applied to this method specifically as named in that task).
- `GetWireInstructionsAsync` includes `currency` always, and `walletId` only when non-null (asserted
  via the recorded outbound request URI, not just the deserialized response).
- Response-mapping fixtures for both methods, built from the verified field lists in §5.

**Api** (WebApplicationFactory + Testcontainers, `LinkedBankAccountsEndpointsTests.cs`):

- `POST /linked-bank-accounts` — Admin succeeds and returns `Pending`; SubAccount caller gets 403
  `tenant-forbidden` (well, more precisely the `TenantForbiddenException` message this controller
  throws — "Only Admin may create linked bank accounts").
- `GET /linked-bank-accounts`, `GET /linked-bank-accounts/{id}` — same Admin-only gate.
- `GET /linked-bank-accounts/{id}/instructions` full round trip: Admin with no `walletId` gets
  Distributor instructions; Admin with an explicit `walletId` gets entity-scoped instructions;
  SubAccount caller with no `walletId` gets its own entity-scoped instructions (implicit self-scope,
  mirroring `01-tenancy-and-authorization.md` §2.4's resolution table); SubAccount caller naming
  another tenant's `walletId` gets 403.
- Full lifecycle integration: `POST` (mock mode) → `wire` webhook fires (mock dispatcher) →
  `GET /linked-bank-accounts/{id}` reflects `Active` → `GET .../instructions` succeeds (a `Pending`
  account's instructions call is not blocked by this file's design, since Circle's own instructions
  endpoint does not require `complete` status to answer — flagged as unverified in §10, not assumed
  either way without a source).

---

## 10. Open corrections / decisions log

| # | Item | Status | Source |
|---|---|---|---|
| 1 | `wire` webhook topic is a **named, documented** Circle Mint topic (`pending`/`complete`/`failed`), not merely inferable from other pages' behavior. | **Confirmed** — the assignment brief asked to check whether this was inferable-only; live fetch shows it is a first-class row in Circle's own webhook-topics table with its own status table and example payload, same as `deposits`/`transfers`/`payouts`. PRD's "added 2026-07-17" phrasing describes when *this project* discovered/added handling for it, not that Circle's documentation of it is new or thin. | `https://developers.circle.com/circle-mint/references/webhook-notifications`, cross-checked against local mirror `docs/circle-mint-docs/reference/webhook-notifications.md` (both fetched/read live this session). |
| 2 | Wire instructions endpoint requires a `currency` query parameter. | **New fact, not in PRD §5.2/Appendix B or either Phase doc.** Neither source lists `currency` as a parameter on `GET .../instructions` — both show only `walletId`. **Resolved: added to `GetWireInstructionsGatewayRequest`/`GetWireInstructionsQuery` (§3.2, §3.5) and the controller route (§4) as a required parameter**, since omitting a required Circle parameter would make every real-mode call fail. | Live fetch, `https://developers.circle.com/api-reference/circle-mint/account/create-business-wire-account`-adjacent instructions reference. |
| 3 | `IStablecoinGateway.GetWireInstructionsAsync` did not exist on any port in Phase 1; Phase 3 Task 3 explicitly deferred the decision ("confirm which port... before adding"). | **Resolved by this file** — placed on `IStablecoinGateway` (§3.2), with a `Grep` of shipped `src/` for `WireInstructions`/`GetWireInstructions` confirming zero prior placements on either port before this file's design was written. Not yet implemented in code; this is a design decision for Phase 3 Task 3 to consume, not a correction of existing shipped code. | `Phase_3_Circle_Integration_Plan.md` line 90; `Grep` of `src/` (this session, zero matches). |
| 4 | `CreateLinkedBankAccountGatewayRequest` (Phase 1 shape) has no `IdempotencyKey` field, contradicting CLAUDE.md invariant 11 and Phase 3 Task 4's own audit scope (which names this exact method). | **Corrected here** — `IdempotencyKey` added as the request's first field (§3.2), sourced from the command's `IdempotencyKey`, itself expected to be the caller-reserved key per invariant 11's forwarding clause. No code exists yet to diverge from this — flagged so Phase 1/3 implementers don't copy the Phase 1 doc's field-less record verbatim. | `Phase_1_Feature_Slices.md` line 7251-7252 (no `IdempotencyKey`) vs. `.claude/CLAUDE.md` invariant 11 and `Phase_3_Circle_Integration_Plan.md` line 102 (names `CreateLinkedBankAccountAsync` in the idempotency-forwarding test list). |
| 5 | Real Circle wire-creation request body is **nested** (`billingDetails`, `bankAddress`, three bank-location-dependent schemas) — Phase 1's domain `LinkedBankAccount`/gateway DTOs use four **flat** fields (`BeneficiaryName`, `AccountNumber`, `RoutingNumber`, `BankName`) that map onto only a subset of the real US schema's required fields (`billingDetails.city/country/line1/postalCode`, `bankAddress.country` have no home in the flat shape). | **Open, not resolved by this file** — flagged rather than silently widening the domain entity, since that is a schema/entity-shape decision affecting migrations and out of scope for a docs-only pass. Phase 1's flat shape is adequate for mock mode (§6, which invents no such fields) and for every Application-tier concern (identity, status); it becomes a real blocker only at the Infrastructure/HTTP boundary (§7), which is exactly where this discrepancy is called out. Two resolutions are possible: (a) widen `LinkedBankAccount`/`CreateLinkedBankAccountCommand` to carry the full nested US-schema fields, or (b) keep the domain shape flat and source the missing billing/bank-address fields from static Distributor-level configuration (plausible if the Distributor always links its own bank accounts under one fixed billing identity) — this file does not pick between them. | `https://developers.circle.com/api-reference/circle-mint/account/create-business-wire-account` (live, this session) vs. `Phase_1_Feature_Slices.md` line 7183-7195, 7251-7255. |
| 6 | `GetWireInstructionsAsync`/`GetWireInstructionsQuery` return `LinkedBankAccount` **domain-adjacent** DTOs, not the Domain entity itself — but `ListLinkedBankAccountsQueryHandler`/`GetLinkedBankAccountQueryHandler` (§3.4) return the `LinkedBankAccount` Domain entity directly through `IQueryHandler<..., LinkedBankAccount>`, which the controller (§4) then serializes as the HTTP response body verbatim. | **Standing tension with CLAUDE.md invariant 5** ("Domain entities never leak past the Application boundary into an API response"), carried forward unresolved from shipped Phase 1 code and from the same pattern already flagged (without resolution) in `04-ledger-and-balances.md` §4.1's `GetTransactionQueryHandler`. Not fixed in this file — a fix would mean adding an explicit response-DTO mapping layer to `LinkedBankAccountsController`, which is a code change, not a doc correction; recorded here so it isn't rediscovered as a fresh defect later. | `Phase_1_Feature_Slices.md` line 7679-7703, `04-ledger-and-balances.md` §4.1 (same pattern, same non-fix). |
| 7 | Whether `GET .../instructions` requires the `LinkedBankAccount` to already be `Active`/`complete`, or answers for a still-`Pending` account too. | **Unverified, left open** (§9's test list flags rather than assumes). Neither the live fetch nor the local mirror states a status precondition on the instructions call; PRD §5.1 doesn't address it either. If Circle in fact 4xxs for a non-`complete` bank account, `GetWireInstructionsQueryHandler` (§3.5) needs a pre-check against `account.Status` before calling the gateway — not added here without a source confirming the requirement exists. | No source found either way; flagged for Phase 3 Task 3 to confirm against the live sandbox before shipping. |

No discrepancy was found between PRD §5, `Phase_1_Feature_Slices.md` Task 11's `LinkedBankAccount`/
`wire`-topic portions, and `Phase_3_Circle_Integration_Plan.md` Task 3's banking-relevant bullets on
the core mechanics this file covers (async verification, three-value status, Distributor-level
non-tenant entity, flat-artifact wire instructions) — the gaps found (#2, #3, #4, #5, #7) are all
places where the source docs are silent or the plan never assigned a shape, not places where they
contradict each other or a live-verified fact.
