# Feature: Redemption & Payouts

Source: `docs/PRD.md` §8 (Capability: Redemption/Payouts), Appendix B; `docs/Phase_1_Feature_Slices.md`
Task 11 ("Redemption rework (gross/fees/net) + LinkedBankAccount", PRD §5/§8); `docs/Phase_3_Circle_Integration_Plan.md`
Task 3's `RedeemAsync` row; ADR 0001 (module boundaries — Ledger).

This file owns the **redemption/payout** side only: `RedeemRequest`, `CreateRedemptionCommand`,
`ProcessPayoutStatusCommand`, the `RedemptionsController`, and the `payouts` webhook topic. It
does not define `LinkedBankAccount` itself, its `wire`-topic verification lifecycle, or the
`LinkedBankAccountsController` — those belong to `08-banking-and-wire-instructions.md`; this file
consumes `LinkedBankAccount` only as redemption's destination-account type (`LinkedBankAccountId`
on the request, `LinkedBankAccountStatus.Active` as a precondition). It also does not reimplement
`FundAccount`/`Transaction`/`BalanceSnapshot` mutation mechanics — those are the shared
ledger-posting substrate in `04-ledger-and-balances.md`; redemption's fund-account debit follows
that file's §3.4 "debit on confirmed `Complete`, not at request time" rule, same as transfers.

## 1. Scope / PRD requirement

PRD §8 operations, all Admin or the owning SubAccount:

| Operation | Notes |
|---|---|
| Create redemption | Idempotent. Source = sub-account wallet; destination = verified `LinkedBankAccount`. Rejected if the linked bank account is not `Active` or the sub-account's `FundAccount` balance is insufficient. |
| List / get redemptions | Status via `payouts` webhook: `pending → complete \| failed`. |

**Fee handling is explicit** (PRD §8, mandatory ground truth): the provider deducts its
Institutional Direct flat fee at the point of redemption, so the settled net differs from the
requested gross `amount`. The ledger **always** records three figures — gross, fees, net — never
just net; history/reporting queries expose all three separately, not a collapsed single amount.

**No cancel.** There is no provider-supported cancel once a redemption is submitted (PRD §8) — the
API surface has no `DELETE`/cancel route and no handler attempts one.

**Distinct from crypto payouts.** PRD §8.1 and Appendix B: this feature uses only
`POST /v1/businessAccount/payouts` (fiat wire redemption, Institutional Direct). It never calls
`POST /v1/payouts`, the separate Travel-Rule-gated crypto payout endpoint (`source.identities[]`,
`purposeOfTransfer` — see §5 below). The two are different products at different URLs; this
service has no code path that can reach the crypto one.

Sequence (PRD §8):

```
Portal/API consumer -> TreasuryServiceOrchestrator: Create redemption (idempotency key)
TreasuryServiceOrchestrator -> Circle: POST /v1/businessAccount/payouts
  { source: {type:"wallet", id: walletId}, destination: {type:"wire", id: bankId} }
Circle -> TreasuryServiceOrchestrator: payout accepted (pending)
Circle -> TreasuryServiceOrchestrator (async): webhook payouts { complete, amount, fees, toAmount }
Portal/API consumer -> TreasuryServiceOrchestrator: Get redemption (gross/fees/net)
```

## 2. Domain design

### 2.1 `RedeemRequest` entity

```csharp
// src/TreasuryServiceOrchestrator.Domain/RedeemRequest.cs
namespace TreasuryServiceOrchestrator.Domain;

public class RedeemRequest
{
    public Guid Id { get; set; }
    public required string ClientCompanyId { get; set; }
    public Guid SubAccountId { get; set; }
    public Guid LinkedBankAccountId { get; set; }
    public required string CircleRedeemId { get; set; }
    public Money GrossAmount { get; set; }
    public Money? Fees { get; set; }
    public Money? NetAmount { get; set; }
    public TransferStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public required string CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

`Fees`/`NetAmount` are `Money?` — `null` until the `payouts` webhook confirms `Complete`; a
`Pending` or `Failed` redemption never had a settled fee/net figure to record. `Status` reuses
`Domain.TransferStatus` (`Pending, Complete, Failed`) rather than a redemption-specific enum — same
three-value convention as `TransactionStatus` and `TransferStatus`, per `04-ledger-and-balances.md`
§2.1: the real `payouts` webhook's `running` intermediate event (if Circle ever emits one for
payouts, mirroring transfers) maps to `Pending`, no separate state.

`LinkedBankAccountId` references `08-banking-and-wire-instructions.md`'s `LinkedBankAccount` —
this file only requires that entity expose `Id`, `Status` (`LinkedBankAccountStatus.Active` is the
create-time precondition), and `CircleBankAccountId` (the provider-side wire destination id
forwarded on `RedeemAsync`). `LinkedBankAccount` carries no `ClientCompanyId` (Distributor-level,
shared across tenants) — file 08's concern, not repeated here.

EF mapping (`OnModelCreating`, owned by Infrastructure, not detailed here beyond the shape):
`GrossAmount` is a required `ComplexProperty`; `Fees`/`NetAmount` are nullable `ComplexProperty`s
(`fees_value decimal(18,6)` + `fees_currency_code`, same for net) — never a raw nullable `decimal`.
A unique index on `CircleRedeemId` backs webhook idempotency lookup, mirroring
`Transaction.ProviderReferenceId` in `04-ledger-and-balances.md` §2.2.

### 2.2 Why gross is reserved, not debited, at creation

Redemption follows the same pattern as transfers (`10-transfers.md`) and `04-ledger-and-balances.md`
§3.4: `CreateRedemptionCommandHandler` only *validates* sufficient `FundAccount.Balance` at
creation time — it does not call `LedgerPostingService.PostAsync`. The debit happens later, in
`ProcessPayoutStatusCommandHandler`, only once the `payouts` webhook reports `Complete`, and it
debits by the **gross** amount reserved at creation, not whatever figure the webhook happens to
report — a webhook race or provider rounding must not change how much the ledger takes out. A
`Failed` redemption never touched the balance, so no reversal is needed.

## 3. Application design

Module: `Ledger` (ADR 0001). Shipped path convention (corrected this session — `Ledger`-prefixed,
not a flat `Application/Redemptions/`): `Application/Ledger/Redemptions/`.

### 3.1 `CreateRedemptionCommand` / `CreateRedemptionCommandHandler`

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Redemptions/CreateRedemptionCommand.cs
namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

public sealed record CreateRedemptionCommand(
    string ResolvedClientCompanyId, Guid LinkedBankAccountId, Money GrossAmount,
    string IdempotencyKey, string CorrelationId);

public sealed record CreateRedemptionResult(
    Guid RedemptionId, string CircleRedeemId, Money GrossAmount, TransferStatus Status);
```

`CreateRedemptionCommandHandler(ISubAccountRepository, ILinkedBankAccountRepository,
IFundAccountRepository, IRedeemRequestRepository, IStablecoinGateway, IIdempotencyService,
IAuditLogService, IUnitOfWork)`:

1. Validate (`GrossAmount.Amount > 0`, currency/ids non-empty).
2. Resolve the `SubAccount` for `ResolvedClientCompanyId` (`NotFoundException` if none).
3. Resolve the `LinkedBankAccount` by id (`NotFoundException` if none); `ConflictException` if its
   `Status != Active` — an unverified or failed-verification destination account can never be a
   redemption target.
4. Resolve the tenant's `FundAccount`; `ConflictException` if the currency mismatches or
   `Balance.Amount < GrossAmount.Amount` — insufficient funds is rejected before any gateway call,
   not discovered after Circle accepts the payout.
5. `IdempotencyExecutor.ExecuteAsync` (reserve → gateway call → persist → complete, CLAUDE.md
   invariant 11, two `SaveChangesAsync`): calls `IStablecoinGateway.RedeemAsync`, then writes a
   `RedeemRequest` row with `Status = Pending`, `Fees = null`, `NetAmount = null`, and an audit
   entry (`"RedemptionCreated"`).

No `LedgerPostingService.PostAsync` call and no `Transaction` row here — per §2.2, the balance
isn't touched and no ledger `Transaction` is recorded until the webhook confirms completion (the
handler's own `RedeemRequest` row is the pending record; the shared ledger `Transaction` row is
written alongside the debit in §3.3, matching how `10-transfers.md`'s
`ProcessTransferStatusCommandHandler` writes its `Transaction` at debit time, not creation time).

### 3.2 `ListRedemptionsQuery` / `GetRedemptionQuery`

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Redemptions/ListRedemptionsQuery.cs
public sealed record ListRedemptionsQuery(string ResolvedClientCompanyId);
// Handler: resolves SubAccount, then IRedeemRequestRepository.ListForSubAccountAsync(subAccount.Id, ct).

// src/TreasuryServiceOrchestrator.Application/Ledger/Redemptions/GetRedemptionQuery.cs
public sealed record GetRedemptionQuery(string ResolvedClientCompanyId, Guid RedemptionId);
// Handler: IRedeemRequestRepository.GetByIdAsync(RedemptionId, ResolvedClientCompanyId, ct)
//     ?? throw NotFoundException — tenant isolation via identity check, same idiom as
//     04-ledger-and-balances.md §4.1's GetTransactionQueryHandler (cross-tenant guess reads as
//     not-found, never leaks existence).
```

`IRedeemRequestRepository` (`Application/Ledger/Ports/IRedeemRequestRepository.cs`):

```csharp
public interface IRedeemRequestRepository
{
    Task AddAsync(RedeemRequest request, CancellationToken cancellationToken);
    Task<RedeemRequest?> GetByIdAsync(Guid id, string clientCompanyId, CancellationToken cancellationToken);
    Task<RedeemRequest?> GetByCircleRedeemIdAsync(string circleRedeemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RedeemRequest>> ListForSubAccountAsync(Guid subAccountId, CancellationToken cancellationToken);
}
```

Every lookup is tenant- or identity-scoped — no unscoped `GetByIdAsync(Guid, ct)` exists, matching
`ITransferRepository`'s shape.

### 3.3 `ProcessPayoutStatusCommand` / `ProcessPayoutStatusCommandHandler`

```csharp
// src/TreasuryServiceOrchestrator.Application/Ledger/Redemptions/ProcessPayoutStatusCommand.cs
namespace TreasuryServiceOrchestrator.Application.Ledger.Redemptions;

// NetAmount is non-nullable here: the optional-toAmount-vs-computed-fallback branching (§5)
// happens once, at the webhook mapping edge in PayoutsWebhookTopicProcessor, not in this command.
public sealed record ProcessPayoutStatusCommand(
    string CircleRedeemId, string Status, Money GrossAmount, Money Fees, Money NetAmount);

public sealed record ProcessPayoutStatusResult(Guid RedemptionId, TransferStatus Status);
```

`ProcessPayoutStatusCommandHandler(IRedeemRequestRepository, IFundAccountRepository,
IBalanceSnapshotRepository, IAuditLogService, IUnitOfWork)`:

1. Look up the `RedeemRequest` by `CircleRedeemId` (`NotFoundException` if none — a webhook for a
   redemption this service never created).
2. Map `command.Status` to `TransferStatus` (reuses `Ledger.Transfers`' internal
   `TransferStatusMapper` — same assembly, no duplicated switch statement). If the mapped status
   already equals the current one, return early with no mutation — webhook re-delivery is
   idempotent, not a second debit.
3. On `Complete`: set `Fees`/`NetAmount` from the command, resolve the tenant `FundAccount`, debit
   `Balance` by `redeemRequest.GrossAmount` (the amount reserved at creation, §2.2 — not
   `command.GrossAmount`, which is only used for the `Transaction`/audit record, not the debit
   math), and write a `BalanceSnapshot(Reason = PostMutation)` carrying the resulting balance.
4. On `Failed`: no `FundAccount` mutation (nothing was ever debited).
5. Append an audit entry (`"RedemptionStatusChanged"`) and `SaveChangesAsync` once.

**Resolved 2026-07-17 grilling (ticket 07): this handler routes the gross debit through
`LedgerPostingService.PostAsync`**, not the inline triplet the Phase 1 source snippet shows —
applying ticket 12's already-ratified `PostAsync(signed Money)` shape (`04-ledger-and-balances.md`
§6), since redemption debit is one of that module's three named callers (deposit credit, transfer
debit, redemption debit). Step 3 above becomes: on `Complete`, call
`LedgerPostingService.PostAsync(new LedgerPosting(fundAccount.Id, -redeemRequest.GrossAmount.
Amount, redeemRequest.GrossAmount.CurrencyCode, TransactionType.Redemption, redeemRequest.
CircleRedeemId))` in place of the hand-rolled `Transaction`/`Balance`/`BalanceSnapshot` writes —
the module owns posting the `Transaction`, adjusting `FundAccount.Balance`, and writing the
`BalanceSnapshot` internally. No separate `IBalanceSnapshotRepository`/`IFundAccountRepository`
mutation call remains in this handler.

### 3.4 `RedemptionsController`

```csharp
// src/TreasuryServiceOrchestrator.Api/Ledger/RedemptionsController.cs
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sub-accounts/{clientCompanyId}/redemptions")]
public sealed class RedemptionsController(
    ICallerContext caller,
    ICommandHandler<CreateRedemptionCommand, CreateRedemptionResult> createHandler,
    IQueryHandler<ListRedemptionsQuery, IReadOnlyList<RedeemRequest>> listHandler,
    IQueryHandler<GetRedemptionQuery, RedeemRequest> getHandler) : ControllerBase
{
    // POST   .../redemptions        -> CreateRedemptionCommand, 201 CreatedAtAction(Get)
    // GET    .../redemptions        -> ListRedemptionsQuery
    // GET    .../redemptions/{id}   -> GetRedemptionQuery
}
```

Tenant scope resolved via `(TenantScope.SingleTenant)TenantScopeResolver.Resolve(caller,
clientCompanyId)` (a route with an explicit `clientCompanyId` segment always resolves
`SingleTenant`) — same pattern as `04-ledger-and-balances.md` §4.1. `CreateRedemptionRequest`
(request DTO) carries `Guid LinkedBankAccountId, decimal GrossAmount, string CurrencyCode, string
IdempotencyKey`; the controller maps `GrossAmount`/`CurrencyCode` into a single `Money` before
constructing the command — the API never accepts a raw `decimal` past the controller boundary.
Every mutating request validates via the standard FluentValidation filter (CLAUDE.md invariant 6);
the controller itself does not branch on domain exceptions — those become RFC 7807 responses
centrally.

## 4. Circle provider mapping (verified)

`RedeemAsync` and `CreateLinkedBankAccountAsync` live on `IStablecoinGateway`
(`Application.Ledger.Ports`), implemented by `CircleMintGateway` (real) /
`MockStablecoinGateway` (mock) — per `Phase_3_Circle_Integration_Plan.md` Task 3 and
`02-mock-mode.md` §2's gateway split (money-moving operations belong on the Ledger module's
gateway, not Compliance's `ISubAccountGateway`).

| Gateway method | Circle endpoint | Request | Response |
|---|---|---|---|
| `RedeemAsync` | `POST /v1/businessAccount/payouts` | `idempotencyKey`; `source: {type:"wallet", id: walletId}` (**optional** — see §5); `destination: {type:"wire", id: bankId}` (required; the schema's `BusinessDestinationRequest` also supports `cubix`/`pix`/`sepa`/`sepa_instant`, but this product only ever sets `wire`) | `amount` (gross), `fees`, `toAmount` (**optional** — see §5) |
| List redemptions (reconciliation/backfill, `04-ledger-and-balances.md` §1) | `GET /v1/businessAccount/payouts?sourceWalletId=…` | query | list of payout resources |
| Redemption status events | `payouts` webhook topic | — | `pending → complete \| failed` |

`GatewayDtos.cs` shapes (`Application/Ledger/Ports/GatewayDtos.cs`):

```csharp
public sealed record RedeemGatewayRequest(
    string IdempotencyKey, string SourceWalletId, string DestinationBankAccountId, Money GrossAmount);

public sealed record GatewayRedeemResult(string CircleRedeemId, string Status);
```

`IStablecoinGateway.RedeemAsync(RedeemGatewayRequest, CancellationToken) : Task<GatewayRedeemResult>`
— the gateway always accepts and forwards `SourceWalletId` explicitly; it is never left to Circle's
default (§5).

## 5. Live Circle-fact verification (2026-07-17)

Verified by fetching Circle's hosted OpenAPI specs directly (`https://developers.circle.com/openapi/account.yaml`
for the institutional `businessAccount/payouts` endpoint, `https://developers.circle.com/openapi/payouts.yaml`
for the crypto `/v1/payouts` endpoint) — the same specs the PRD's 2026-07-16/17 passes cite as
definitive (`docs/PRD.md` line 610).

| Fact | Result |
|---|---|
| `source` optional on `POST /v1/businessAccount/payouts`, defaults to Master Account wallet | **Confirmed.** `source` is not in `BusinessPayoutCreationRequest`'s `required` array; schema note: "If not provided, the deposit will come from the main wallet." Same hazard as transfers (PRD line 326) — the gateway must set `source` explicitly on every call. |
| `destination` shape | `type: wire` (among `cubix`/`pix`/`sepa`/`sepa_instant` — this product only uses `wire`, matching PRD §8's fiat-wire-only scope). |
| `toAmount` optional on the payout response, "used when requesting currency exchange" | **Confirmed**, framing unchanged. `toAmount` (`FiatPayoutToMoney`) appears alongside `amount`/`fees` as an optional response field; description: "To be used when requesting currency exchange." Net = `toAmount` when present, else computed `amount − fees` (§3.3, §6). |
| `POST /v1/businessAccount/payouts` has no Travel Rule fields | **Confirmed.** Neither `BusinessPayoutCreationRequest` nor the `BusinessPayout`/`BusinessPayoutDetail` response schemas contain `identities[]`/`purposeOfTransfer` or similar. |
| `POST /v1/payouts` (crypto, product-unused) has Travel Rule fields | **Confirmed, for contrast only.** `TransferSourceWalletLocation.identities[]` (array of `Identity`) and `purposeOfTransfer` (`CryptoPayoutPurposeOfTransfer`, required for Singapore entities) are present on the crypto endpoint. This service has no code path calling `/v1/payouts` — confirms PRD §7.3/Appendix B's two-endpoint split still holds. |

No discrepancy found against the PRD's existing 2026-07-16/17 verified claims; this pass
re-confirms them against the live spec rather than correcting anything.

## 6. Mock-mode behavior

See `02-mock-mode.md` for the full mock-provider design (production guard, failure/latency
injection, real-webhook-pipeline delivery). Redemption-specific mock behavior
(`MockStablecoinGateway.RedeemAsync`):

1. Optional injected latency/failure (`ProviderUnavailableException`) via `MockProviderOptions`.
2. Generates `circleRedeemId = $"redeem-{Guid.NewGuid():N}"`, computes `fees =
   MockProviderOptions.RedemptionFlatFeeAmount` (default `1.50m`) and `toAmount = GrossAmount -
   fees`.
3. Schedules one `payouts` webhook (`{"payout":{"id":..., "status":"complete", "amount":...,
   "fees":..., "toAmount":..., "currency":...}}`) through `IMockWebhookScheduler`, delivered via
   the real inbox → dedup → `PayoutsWebhookTopicProcessor` pipeline after
   `WebhookDelayMilliseconds` — mock mode is a producer feeding the real pipeline, not a shortcut
   around it (`02-mock-mode.md` §1).
4. Returns `GatewayRedeemResult(circleRedeemId, "pending")` synchronously — the caller never
   observes `Complete` on the initial call, matching the real provider's async settlement.

`MockProviderOptions.RedemptionFlatFeeAmount` (`decimal`, default `1.50m`) is the mock's only
redemption-specific config; it exists purely to make the gross/fees/net triplet exercisable
end-to-end without a real Circle sandbox (PRD §15.1 demo script: "redemption completes showing
gross/fees/net").

## 7. Real Circle HTTP integration

`CircleMintGateway.RedeemAsync` (`Infrastructure/Providers/Circle/CircleMintGateway.cs`), per
`Phase_3_Circle_Integration_Plan.md` Task 3:

- `POST /v1/businessAccount/payouts` via `IHttpClientFactory` (CLAUDE.md invariant 3 — never `new
  HttpClient()`), body: `idempotencyKey`, `source: {type:"wallet", id: request.SourceWalletId}`
  **always set explicitly** — never omitted, even though the field is optional on the wire (§5);
  omitting it would silently debit the Distributor's Master Account wallet instead of the
  sub-account's, a hard invariant with a dedicated test (§8), not a convention. `destination:
  {type:"wire", id: request.DestinationBankAccountId}`.
- Idempotency key forwarded on every call (CLAUDE.md invariant 11; PRD §11.3) — the same
  `IdempotencyKey` reserved by `IdempotencyExecutor` in `CreateRedemptionCommandHandler`, not a
  gateway-internal one.
- Response mapped to `GatewayRedeemResult(CircleRedeemId: response.Id, Status: response.Status)` —
  `status` at creation is always `"pending"`; gross/fees/net settle later via the `payouts`
  webhook, never read from the synchronous create response.
- Standard resilience wrapper (timeout, retry+backoff, circuit breaker — PRD §11.3) applies here
  same as every other money-moving gateway call; not redefined per-method.

`PayoutsWebhookTopicProcessor` (`Infrastructure/Webhooks/PayoutsWebhookTopicProcessor.cs`), topic
`"payouts"`:

1. Deserializes `{"payout": {id, status, amount, fees, toAmount?, currency}}`. `id`/`status`/
   `amount`/`fees`/`currency` are required — missing any throws `InvalidOperationException`.
   `toAmount` is **intentionally excluded** from that required-field check (§5's confirmed
   optionality) — its absence must not throw.
2. Parses `amount`/`fees` as `decimal` (`CultureInfo.InvariantCulture`); computes `netAmount =
   toAmount is null ? amount - fees : toAmount` — the one place this optional-vs-computed branch
   happens (§3.3).
3. Invokes `ProcessPayoutStatusCommandHandler` with `Money`-wrapped gross/fees/net, all in the
   webhook's reported `currency`.

## 8. Tests required

| Layer | File | Covers |
|---|---|---|
| Unit | `CreateRedemptionCommandHandlerTests.cs` | Creates `RedeemRequest` `Pending`, `Fees`/`NetAmount` both `null`, debits nothing yet. `ConflictException` when `LinkedBankAccount.Status != Active`. `ConflictException` when `FundAccount.Balance` insufficient or currency mismatched. |
| Unit | `ProcessPayoutStatusCommandHandlerTests.cs` | `Complete`: sets `Fees`/`NetAmount`, debits `FundAccount.Balance` by the **reserved gross** (not the webhook-reported amount), writes one `BalanceSnapshot(PostMutation)`. Idempotent re-delivery (status already matches) performs no mutation, no `SaveChangesAsync`. `NotFoundException` when no `RedeemRequest` matches `CircleRedeemId`. `Failed`: no `FundAccount` mutation. |
| Unit | `PayoutsWebhookTopicProcessorTests.cs` | `Topic == "payouts"`. Deserializes and invokes the handler with `NetAmount == toAmount` **when `toAmount` present**. Deserializes and invokes with `NetAmount == amount - fees` **when `toAmount` absent** — the explicit branch this file's ground truth requires a dedicated test for. Throws `InvalidOperationException` on a payload missing `id`/`status`/`amount`/`fees`/`currency` (missing `toAmount` alone must not throw — covered by the two prior cases, not a third failure case). |
| Unit | `CircleMintGatewayRedeemTests.cs` (Phase 3) | Asserts the outbound JSON body's `source` is always present with the caller-supplied `SourceWalletId` — never omitted, even on a request built without an explicit source upstream (the hard invariant from §7). Asserts `idempotencyKey` in the outbound body equals the value passed into `RedeemGatewayRequest`, not a value generated inside the gateway (Phase 3 Task 3's cross-gateway idempotency test requirement). |
| Unit | `MockStablecoinGatewayRedeemTests.cs` | `RedeemAsync` schedules exactly one `payouts` webhook and returns `Status == "pending"`. Injected failure rate throws `ProviderUnavailableException`. |
| Integration | `RedemptionsEndpointsTests.cs` | Full round trip against a real Testcontainers SQL Server + mock provider: create a `LinkedBankAccount`, poll it to `Active` (async `wire` verification, owned by file 08), `POST .../redemptions`, poll `GET .../redemptions/{id}` to `Complete`, assert `Fees`/`NetAmount` both populated and `GrossAmount` unchanged. Cross-tenant isolation: a different `ClientCompanyId` header gets 403/404 on another tenant's redemption, never leaks it. |
| Integration | `TransactionsAndBalancesEndpointsTests.cs` (owned by `04-ledger-and-balances.md`, exercises this file's debit) | A completed redemption debits `GET .../balances` by the gross amount and appears in `GET .../transactions` as `Type = Redemption`. |

## 9. Open corrections / decisions log

- **`toAmount` optionality and framing — re-confirmed live, no change.** PRD line 341 already
  states "`toAmount` is optional on the payout resource... used when requesting currency exchange,"
  verified 2026-07-17. This file's §5 re-fetched `account.yaml` directly and found the identical
  framing — documented as re-confirmation, not a new finding.
- **`source` omission hazard — re-confirmed live for `businessAccount/payouts` specifically.** PRD
  line 326 already states this for both `transfers` and `payouts`; §5 confirms the `payouts` half
  against the live schema's exact wording ("If not provided, the deposit will come from the main
  wallet"). No discrepancy.
- **`destination` is not `wire`-exclusive at the schema level.** Not previously called out in the
  PRD or Phase 1 source: `BusinessDestinationRequest` also supports `cubix`/`pix`/`sepa`/
  `sepa_instant` destination types. This product's redemption feature only ever constructs `{type:
  "wire", id: bankId}` (PRD §8 is fiat-wire-only) — flagged here as a schema-breadth note, not a
  scope change; nothing in this file's design should be read as supporting non-wire destinations.
- **`ProcessPayoutStatusCommandHandler`'s debit logic — resolved 2026-07-17 grilling (ticket 07).**
  Routes through `LedgerPostingService.PostAsync` (signed negative `Money` for the gross debit),
  not the inline triplet the Phase 1 source snippet shows. See §3.3's updated text for the exact
  call shape. This applies ticket 12's already-ratified `PostAsync` signature
  (`04-ledger-and-balances.md` §6) — redemption debit is one of that module's three named callers
  (deposit credit, transfer debit, redemption debit).
- **`RedeemRequest.Status` reuses `TransferStatus` rather than a redemption-specific enum.** Not a
  discrepancy — this is the Phase 1 source's own design (`Status TransferStatus` field on
  `RedeemRequest`) and matches `04-ledger-and-balances.md` §2.1's three-value-status convention
  across transfers and redemptions. Documented here for clarity since a reader might expect a
  `PayoutStatus` type; none exists or is needed.
- No other discrepancies found between PRD §8, `Phase_1_Feature_Slices.md` Task 11's
  redemption-specific steps, `Phase_3_Circle_Integration_Plan.md` Task 3's `RedeemAsync` row, and
  the live `account.yaml`/`payouts.yaml` specs during this pass.
