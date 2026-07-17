# Feature: Reliability & Error Handling (cross-cutting)

Source: `docs/PRD.md` §11, `docs/Phase_1_Feature_Slices.md` Task 2 (error-taxonomy portion —
tenant-scope-resolution portion of Task 2 lives in `01-tenancy-and-authorization.md`, not here),
`docs/Phase_3_Circle_Integration_Plan.md` Task 1 / Task 7, `docs/DepositReconciliationPLan.md`
(folded in below), `docs/adr/0006-deposit-listing-on-stablecoin-gateway.md`,
`docs/circle-mint-docs/reference/error-codes.md`, `docs/circle-mint-docs/reference/idempotent-requests.md`.

## 1. Scope / PRD requirement

PRD §11 (Cross-cutting: Reliability, Consistency & Reconciliation) sets four requirements this
file covers end to end:

- **§11.1 Idempotency** — every mutating consumer operation accepts a caller-supplied idempotency
  key; retries with the same key return the original outcome and never double-execute. The key is
  forwarded to the provider on every money-moving call so a crash between our commit and the
  provider call cannot double-spend.
- **§11.2 Error contract** — all errors are RFC 7807 `ProblemDetails` responses with a stable
  six-value taxonomy (`validation | not-found | tenant-forbidden | conflict | provider-rejected |
  provider-unavailable`), never a leaked stack trace or ad-hoc shape.
- **§11.3 Provider resilience** — outbound Circle calls run with timeouts, bounded retry with
  backoff (idempotent calls only), and a circuit breaker; when the circuit is open, mutating
  operations fail fast with `provider-unavailable`.
- **§11.4 Reconciliation (v1 requirement, not roadmap)** — because deposit crediting is
  webhook-driven, a silently missed webhook means funds settle at the provider with no ledger
  record. A scheduled job lists provider-side deposits per wallet, cross-checks against the local
  ledger, self-heals unambiguous gaps, and alerts on anything it cannot resolve.

These four are one feature, not four: idempotency makes retries safe, resilience makes retries
happen, the error taxonomy tells the caller when a retry is safe versus not (`provider-rejected`
= don't retry blindly, `provider-unavailable` = safe to retry), and reconciliation is the backstop
for the one failure mode none of the above can catch — an async webhook that never arrives.

## 2. Idempotency design

### 2.1 Caller-supplied key, forwarded to the provider

Every mutating consumer operation requires a caller-supplied idempotency key (PRD §11.1). Two
distinct idempotency layers exist and must not be conflated:

- **Local (our API's own dedup)**: the key is stored against `(ClientCompanyId, IdempotencyKey)`
  as a unique index on the reservation row created in step 1 of the two-`SaveChangesAsync`
  pattern (§2.2 below). A retry with the same key short-circuits to the original outcome without
  re-invoking the provider.
- **Provider-forwarded (Circle's own dedup)**: on every money-moving Circle call, the *same* key
  reserved locally is forwarded as Circle's `idempotencyKey` request-body field (confirmed —
  Circle's UUID v4 requirement, see §2.3) — never a freshly generated GUID per HTTP attempt, which
  would defeat the point on retry (`Phase_3_Circle_Integration_Plan.md` Task 4 exists solely to
  audit this: every money-moving gateway method must set `idempotencyKey` from the value already
  threaded through the handler, not generate its own).

One notable asymmetry, verified 2026-07-17 in `Phase_3_Circle_Integration_Plan.md` Task 2:
`POST /v1/externalEntities` (entity registration) has **no `idempotencyKey` field at all** —
provider-side dedup there is the 409 on a duplicate `businessUniqueIdentifier` +
`identifierIssuingCountryCode` pair, not a forwarded key. Local idempotency (the two-
`SaveChangesAsync` pattern) still applies; only the provider-forwarding half doesn't exist for
that one endpoint. Every *money-moving* endpoint (transfers, payouts, deposit addresses,
recipients, linked bank accounts) does carry `idempotencyKey` and must forward it.

### 2.2 Reserve → gateway/state-transition → complete (two `SaveChangesAsync`)

Every mutating handler follows this shape (CLAUDE.md invariant 11):

1. **Reserve**: insert a row keyed on `(ClientCompanyId, IdempotencyKey)` inside a transaction,
   `SaveChangesAsync` #1. The unique index is the mechanism that makes concurrent duplicate
   requests fail one and succeed one, deterministically, at the database — not at application
   logic that could race.
2. **Gateway/state-transition**: call the provider (forwarding the idempotency key per §2.1) or
   perform the domain state transition. This step is outside any local transaction — it's an
   external network call, and holding a DB transaction open across it would serialize unrelated
   requests against the same connection pool for no reason.
3. **Complete**: update the reservation row with the outcome, `SaveChangesAsync` #2.

If the process crashes between steps 2 and 3, a retry with the same key finds the reservation row
already present (step 1 short-circuits) but the outcome not yet recorded — the handler must
re-query the provider for the actual outcome (via the forwarded idempotency key, which Circle
also uses for its own dedup) rather than blindly re-executing step 2, which is exactly the
double-spend PRD §11.1 exists to prevent.

### 2.3 Idempotency key is a Circle request-body field — settled, not re-verified this pass

Per the task brief, this fact is treated as settled: `docs/circle-mint-docs/reference/idempotent-requests.md`
notes it was verified live 2026-07-07 at `https://developers.circle.com/api-reference/idempotent-requests`
(the page has since moved from `circle-mint/references/idempotent-requests` to the product-agnostic
`api-reference/` path — same content). Key facts: `idempotencyKey` is a **request-body field**
(not an HTTP header), required as **UUID v4** on endpoints that support it. This pass did not
re-fetch it live — carried forward per the 2026-07-07 verification already on file.

## 3. RFC 7807 error taxonomy + exception types

### 3.1 The six-value taxonomy (PRD §11.2)

| Class | Meaning | HTTP status |
|---|---|---|
| `validation` | Request malformed or violates business rules. | 400 |
| `not-found` | Resource does not exist *within the caller's tenant scope*. | 404 |
| `tenant-forbidden` | Caller attempted to access another sub-account. | 403 |
| `conflict` | Idempotency-key reuse with different payload; illegal state transition. | 409 |
| `provider-rejected` | Provider synchronously refused the operation (mapped provider error code included). | 422 |
| `provider-unavailable` | Provider timeout/outage; safe-to-retry indicated. | 503 |

Every error response is RFC 7807 `ProblemDetails`: `Status`, `Title`, `Detail` (the exception
message), `Type` set to `urn:treasury-service-orchestrator:error:{class}` — a stable, machine-
parseable URN per class, not a free-text string a caller would have to pattern-match.

### 3.2 Exception hierarchy

All domain exceptions derive from `DomainException`, **not** `TreasuryDomainException` — a stale
name from older docs, corrected elsewhere this session and restated here since this file owns the
error-taxonomy narrative:

```csharp
namespace TreasuryServiceOrchestrator.Application.Exceptions;

public abstract class DomainException(string message) : Exception(message);

public sealed class TenantForbiddenException()
    : DomainException("Caller may not act on the requested tenant.");

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);

/// <summary>Terminal provider rejection — retrying will not succeed.</summary>
public sealed class ProviderRejectedException(string message) : DomainException(message);

/// <summary>Retryable provider failure — the provider is temporarily unavailable.</summary>
public sealed class ProviderUnavailableException(string message) : DomainException(message);
```

`TenantForbiddenException` takes no message parameter — it hardcodes a fixed string, since the
detail never varies and a caller-controlled message on a 403 has no legitimate use. The other four
take a message, since `not-found`/`conflict`/`provider-rejected`/`provider-unavailable` all carry
resource- or provider-specific detail.

One documented exception to "every subtype maps by type": `SubAccountAlreadyExistsException`
derives directly from `DomainException` (not from `ConflictException`) because it carries a
structured `ClientCompanyId` property the generic conflict exception doesn't have. It still maps
to 409/`conflict`, just via its own explicit `case` in the handler switch, matched **before** the
generic `ConflictException` case (C# pattern-match `switch` evaluates cases in order — a
`ConflictException` case first would never let the more specific type's case run, since
`SubAccountAlreadyExistsException` also passes the generic conflict shape logically, though not
via inheritance here — the ordering constraint is about explicitness, not variance, since the two
types don't actually share a base).

### 3.3 Global exception handler

A single `IExceptionHandler` implementation is the only place mapping domain exceptions to
`ProblemDetails`; controllers never catch domain exceptions themselves (CLAUDE.md invariant 6):

```csharp
public sealed class TreasuryProblemDetailsExceptionHandler(ILogger<TreasuryProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, type) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "validation"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "not-found"),
            TenantForbiddenException => (StatusCodes.Status403Forbidden, "Tenant forbidden", "tenant-forbidden"),
            SubAccountAlreadyExistsException => (StatusCodes.Status409Conflict, "Sub-account already exists", "conflict"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", "conflict"),
            ProviderRejectedException => (StatusCodes.Status422UnprocessableEntity, "Provider rejected request", "provider-rejected"),
            ProviderUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Provider unavailable", "provider-unavailable"),
            _ => (0, "", ""),
        };

        if (status == 0)
        {
            logger.LogError(exception, "Unhandled exception");
            return false; // falls through to the framework's default 500 handling
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Type = $"urn:treasury-service-orchestrator:error:{type}",
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, ct);
        return true;
    }
}
```

Wired via `builder.Services.AddExceptionHandler<TreasuryProblemDetailsExceptionHandler>()` +
`builder.Services.AddProblemDetails()`, and `app.UseExceptionHandler()` immediately after
`var app = builder.Build();`. Any unmapped exception type returns `false`, logs at `Error`, and
falls through to ASP.NET Core's own unhandled-exception 500 path — it does not silently swallow
a bug as a generic 500 `ProblemDetails`, it lets the framework's normal diagnostics fire, so an
exception type nobody accounted for is loud in logs rather than quietly categorized as
"validation" or similar.

FluentValidation's `ValidationException` maps to `validation` directly — every endpoint has a
validation filter, no controller hand-rolls validation inline (CLAUDE.md invariant 6); the
filter's failures surface as this same exception type, so the mapping is a single `case`, not a
per-endpoint concern.

## 4. Provider resilience (Polly)

Per `Phase_3_Circle_Integration_Plan.md` Task 1, the named `"Circle"` `HttpClient` — resolved via
`IHttpClientFactory`, never `new HttpClient()` (CLAUDE.md invariant 3) — carries a Polly pipeline
matching PRD §11.3 exactly:

```csharp
public sealed class CircleClientOptions
{
    public string BaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerDurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);
}
```

Policy shape:

- **Timeout**: `TimeoutSeconds` per attempt.
- **Retry with exponential backoff**: only on `provider-unavailable`-shaped failures — 5xx, `429`
  (added 2026-07-17 during doc-grilling: rate limiting is safe-to-retry, same class as 5xx/timeout
  — Task 1's retry policy must treat it as retryable), timeout, `HttpRequestException`. **Never**
  on 4xx other than 429 — those are `provider-rejected`, and retrying would resubmit a request
  under an idempotency key that Circle has already bound to a rejected outcome (code `1083`,
  §5 below), turning a legitimate rejection into a confusing conflict.
- **Circuit breaker**: `CircuitBreakerFailureThreshold` consecutive failures opens the circuit for
  `CircuitBreakerDurationOfBreak`. While open, mutating operations fail fast with
  `ProviderUnavailableException` (→ `provider-unavailable`, 503) rather than queuing behind a
  provider that's already down; reads may serve cached/ledger data explicitly marked as such (PRD
  §11.3), which is a call-site decision, not something the circuit breaker itself does.

Retryable calls must be **idempotent** — this is exactly why §2's idempotency-key forwarding
exists: a retried Circle call carries the same `idempotencyKey`, so Circle's own dedup (not just
the retry policy's good intentions) prevents a retried POST from executing twice server-side even
if the first attempt's response was lost to a network failure before this client saw it.

## 5. `CircleErrorTranslator` mapping table (verified)

**Why this exists**: PRD §11.2 defines the six-value taxonomy but no doc maps Circle's actual
numeric/string error codes onto it — Phase 1's mock gateway invents its own errors, so the gap was
invisible until real HTTP calls arrive (`Phase_3_Circle_Integration_Plan.md` Task 7). Every Circle
HTTP error branch in `CircleSubAccountGateway`/`CircleMintGateway` routes through this translator
before the gateway throws — gateways never leak a raw `HttpResponseMessage` or Circle JSON body
past this boundary.

**Module placement**: `CircleErrorTranslator` is conceptually a `Webhooks`-module concern per
Phase 3's Module Boundaries note (error translation and SNS webhook verification are grouped
there in the Application/Domain namespace split, ADR 0001) — but that split applies to
Application/Domain namespaces, not Infrastructure. Infrastructure stays flat (no per-module
subfolder): the file lives at
`src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleErrorTranslator.cs`, same
flat convention as `CircleSubAccountGateway.cs`/`CircleMintGateway.cs`.

### 5.1 Mapping table

Cross-checked against `docs/circle-mint-docs/reference/error-codes.md` and confirmed live (see §6)
on 2026-07-17 — every row below matches the local mirror exactly, no drift found:

| Circle signal | → Product class |
|---|---|
| HTTP `400` + `errors[]` validation array | `validation` |
| HTTP `401`, code `1` (malformed auth) | `provider-unavailable` (config/secret problem, not caller's fault — alert, don't 400 the caller) |
| HTTP `403`, code `3` | `provider-unavailable` (decided 2026-07-17: entitlement/config problem on *our* key, same class as 401 — alert ops, never blame the tenant) |
| HTTP `429` (rate limited) | `provider-unavailable` (safe-to-retry; §4's retry policy treats 429 as retryable alongside 5xx) |
| HTTP `404` / code `5001`/`5004`/`5005` (resource not found variants) | `not-found` |
| HTTP `409`, code `1083` (idempotency key bound to another request), `409` external-entity duplicate | `conflict` |
| Codes Circle documents as "State conflict": `1084`-`1086`, `1097`, `1107`, `2003` (recipient already registered), `2004`, `5003` (destination in 24h inactive hold) — regardless of HTTP status | `conflict` |
| Code `1093`/`5006`/`insufficient_funds` (sync or async) | `provider-rejected` (surface as a specific "insufficient funds" reason, not generic) |
| Async `errorCode: transaction_denied`/`transfer_denied` + `riskEvaluation.decision: denied` | `provider-rejected` (compliance/risk denial — do not auto-retry; needs a corrected resubmission) |
| Generic `code: -1`, HTTP `5xx`, timeout | `provider-unavailable` (safe-to-retry) |

Implementation: `CircleErrorTranslator.Translate(...)` returns the existing PRD §11.2 exception
types from §3.2 above (`ProviderRejectedException`/`ProviderUnavailableException`/etc.) — it does
not invent a parallel error type; it is purely the mapping function from Circle's wire shape to
this API's own taxonomy.

### 5.2 Insufficient-funds and risk-denial detail

Two rows in the table above are intentionally "surface as a specific reason, not generic": code
`1093`/`5006`/`insufficient_funds` should map to a `ProviderRejectedException` whose message names
insufficient funds specifically (not a generic "provider rejected"), and an async
`transaction_denied`/`transfer_denied` with `riskEvaluation.decision: denied` should similarly
name the compliance/risk denial. Both are terminal, `provider-rejected` outcomes — Circle's own
"Recommended treatment" table (§6) says insufficient funds should be resolved by topping up the
source wallet and retrying, and risk denials should be reviewed and resubmitted with a **new**
idempotency key (not retried with the same one, since the provider has already bound that key to
a denied outcome).

## 6. Live-source verification log

1. **Error-code → HTTP-status mapping table** — fetched
   `https://developers.circle.com/circle-mint/references/error-codes` live on 2026-07-17.
   Confirmed against every code named in the task brief:
   - `-1` → HTTP 500, "Transient". Confirmed.
   - `1` → HTTP 401, "Malformed". Confirmed.
   - `3` → HTTP 403, "State conflict". Confirmed.
   - `1083` → HTTP 409 (inferred from the "State conflict" category description, not restated
     per-row on the live page — matches the local mirror, which also states 409 only in the
     category description, not a per-row HTTP column), "State conflict". Confirmed.
   - `1084`, `1085`, `1086`, `1097`, `1107` → "State conflict" (no per-row HTTP status published
     for these — same shape on both live and mirror: only the *category* table publishes HTTP
     status, individual codes in "Core API errors" carry `Treatment` only). Confirmed.
   - `2003`, `2004` → "State conflict". Confirmed.
   - `5001`, `5004`, `5005` → "Malformed". Confirmed.
   - `5003` → "State conflict". Confirmed.
   - `5006` → "Insufficient funds". Confirmed.
   - `1093` → "Insufficient funds". Confirmed.
   - `insufficient_funds` (async `errorCode`, appears on both Stablecoin Payouts and Transfer
     entity errors) → "Insufficient funds". Confirmed.
   - `transaction_denied` / `transfer_denied` → "Compliance: review and resubmit with a new
     idempotencyKey" (i.e. `provider-rejected`, no auto-retry). Confirmed.
   - Institutional Distribution `401` → "State conflict: contact Circle to enable the
     entitlement" (this is the HTTP-403-code-`3` row's *rationale*, not a separate code — the live
     fetch surfaced it under a different endpoint-family table using HTTP 401 instead of 403;
     the mirror's Common-errors table separately lists code `3`/HTTP 403 for the general case.
     No discrepancy — different endpoint families, same "alert ops, don't blame the tenant"
     treatment, both captured in this file's table as one `provider-unavailable` row rather than
     two, since the product-facing translation is identical either way).
   - `429` (rate limited) — **confirmed live 2026-07-17** (follow-up pass), fetched
     `https://developers.circle.com/circle-mint/references/error-codes` directly: "Returned inline
     in the HTTP response with a status code (`400`, `401`, `403`, `404`, `409`, `429`, or `500`)".
     Matches the local mirror exactly. No discrepancy.
   - The live fetch reported no visible "last verified" date on the page itself; the local
     mirror's own note ("Verified live... on 2026-07-07") is the only dated verification on
     record, and this pass's 2026-07-17 fetch found identical content — no drift across the
     10-day gap.
2. **`idempotencyKey` as a Circle request-body field** — **not re-fetched live this pass**, per
   the task brief's instruction that this fact is settled. `docs/circle-mint-docs/reference/idempotent-requests.md`
   records it as verified live 2026-07-07 at (now-moved) `https://developers.circle.com/api-reference/idempotent-requests`;
   treated as current ground truth without re-verification.

**Overall result: no discrepancy found.** Every code in the task's required-verification list is
now confirmed live, including `429` (follow-up pass, 2026-07-17).

## 7. Reconciliation job design (folded in from `DepositReconciliationPLan.md`)

### 7.1 Why this exists

Deposit crediting is webhook-driven (Phase 1's inbox → dedup → per-topic-processor pipeline). A
silently missed webhook — dropped delivery, processor exception swallowed somewhere, SNS
subscription lapsed — means funds settle at Circle with no ledger record: a ledger-vs-custody
drift that is unacceptable for client money (PRD §11.4). Reconciliation is the self-healing
backstop for exactly this failure mode; it does not replace the webhook path, it catches what that
path missed.

### 7.2 Two listing calls per wallet — deposits endpoint carries no on-chain records

Verified 2026-07-17 against live Circle docs (folded in from `DepositReconciliationPLan.md`'s own
correction note, which had previously hardcoded on-chain-only and was doubly wrong): Circle's
`GET /v1/businessAccount/deposits` returns **fiat wire deposits only** — its `type` filter accepts
only `wire`. On-chain deposits arrive on the `transfers` topic instead and must be listed via
`GET /v1/businessAccount/transfers` filtered to the wallet as destination (`destinationWalletId`
query parameter). Reconciliation therefore requires **both** calls per wallet, merged:

```csharp
public sealed record ProviderDepositRecord(
    string ProviderReferenceId,
    string CircleWalletId,
    string DestinationAddress,
    Money Amount,
    DepositSourceType SourceType,   // Wire (from deposits endpoint) or OnChain (from transfers endpoint)
    DateTime OccurredAtUtc);
```

`ListRecentDepositsAsync(string circleWalletId, DateTime sinceUtc, CancellationToken)` models the
union of both calls as one method — callers never issue the two HTTP calls themselves.

### 7.3 Gateway placement — `IStablecoinGateway` (Ledger), not `ISubAccountGateway` (Compliance)

Per `docs/adr/0006-deposit-listing-on-stablecoin-gateway.md`: `ListRecentDepositsAsync` lives on
`IStablecoinGateway` (`Application.Ledger.Ports`, implemented by `CircleMintGateway` /
`MockStablecoinGateway`), **not** on `ISubAccountGateway` (`Application.Compliance.Ports`,
implemented by `CircleSubAccountGateway` / `MockSubAccountGateway`). Deposits are money-moving,
not compliance — the ADR's rationale is the same Compliance/Ledger module boundary this codebase
already draws elsewhere (entity/registration/recipient ops on the Compliance gateway, all
money-moving ops — transfers, redemptions, balance, deposit listing — on the Ledger gateway). An
earlier version of the reconciliation plan had wired this method onto `ISubAccountGateway`
instead; that was a module-boundary violation caught during a docs-sync grill and corrected by
the ADR, not a decision this file is re-litigating.

```csharp
namespace TreasuryServiceOrchestrator.Application.Ledger.Ports;

public interface IStablecoinGateway
{
    Task<IReadOnlyList<ProviderDepositRecord>> ListRecentDepositsAsync(
        string circleWalletId, DateTime sinceUtc, CancellationToken cancellationToken);

    // Transfer/redemption/balance methods also live here — omitted, out of this file's scope.
}
```

`CircleMintGateway.ListRecentDepositsAsync` (Phase 3) issues the two HTTP calls described in §7.2
and merges, tagging each record `Wire` or `OnChain` by source endpoint. In mock mode, the union is
served from an in-memory `IMockProviderDepositLedger` singleton (Infrastructure mock-provider
namespace — not the Application seam, since it has no production adapter; see §7.7) that
`MockStablecoinGateway` delegates to, with a `SeedAsync` test-only entry point for injecting a
"phantom" provider deposit that never produced a webhook.

### 7.4 Reconciliation pass logic

`DepositReconciliationService.RunOnceAsync(CancellationToken)` (Application, `Ledger.Reconciliation`):

1. Compute `sinceUtc = now − LookbackWindowMinutes` (`ReconciliationOptions`, default 1440 minutes
   / 24 hours).
2. List active sub-accounts with a wallet (`ISubAccountRepository.ListActiveWithWalletAsync` —
   excludes inactive, disabled, and walletless sub-accounts; stays on the existing Compliance-
   module repository, this plan only adds the query method, ownership doesn't move).
3. For each sub-account, call `IStablecoinGateway.ListRecentDepositsAsync(walletId, sinceUtc, ct)`.
   A gateway failure for one wallet is caught, logged, and **does not abort the rest of the pass**
   — the remaining sub-accounts still get reconciled this cycle.
4. For each provider deposit record, look up `ITransactionRepository.GetByProviderReferenceIdAsync`.
   If a matching `Transaction` already exists, skip (already recorded via the webhook path — this
   is the dedup check that makes reconciliation safe to run repeatedly without double-crediting).
5. If no match, invoke the **same** `ICommandHandler<ProcessDepositCommand, ProcessDepositResult>`
   the webhook path uses — reconciliation is not a parallel crediting mechanism, it reuses the
   exact handler, so the unique index on `ProviderReferenceId` remains the single dedup safety net
   regardless of which path (webhook or reconciliation) reaches it first. Correlation id for a
   self-healed transaction is literally `reconciliation-{providerReferenceId}` — no other format.
   A per-deposit failure (e.g. the sub-account transitioned to a state that rejects the credit
   between listing and crediting) is caught, logged, and does not abort the pass or the rest of
   that wallet's deposits.
6. Returns the count of transactions self-healed this pass.

Both the per-subaccount gateway-call try/catch and the per-deposit handler-call try/catch are
required — iteration-level and per-item resilience are two different concerns (one wallet's
provider outage vs. one deposit's business-rule rejection), and neither should take down the rest
of the pass.

### 7.5 Background service

`DepositReconciliationBackgroundService` (Infrastructure `BackgroundService`, patterned on the
notification-dispatch background service) polls every `ReconciliationOptions.IntervalSeconds`
(default 300 seconds), each pass opening a fresh DI scope and calling `RunOnceAsync`. Both the
per-pass try/catch and the delay's `OperationCanceledException` handling exist so a single failed
pass or host shutdown never crashes the loop — same hardening pattern as every other polling
background service in this codebase.

### 7.6 Config

```csharp
public sealed class ReconciliationOptions
{
    public int IntervalSeconds { get; set; } = 300;
    public int LookbackWindowMinutes { get; set; } = 1440;
}
```

Bound from an `appsettings.json` `"Reconciliation"` section, consumed by both the Application
service and the Infrastructure background service.

### 7.7 Mock-mode seam correction

`IMockProviderDepositLedger`/`MockProviderDepositLedger` live in the Infrastructure mock-provider
namespace (`Infrastructure/Mocks/`), **not** `Application/Ledger/Ports/` — a design-pass
correction folded in from `DepositReconciliationPLan.md`: a mock-only seam with no production
adapter (`CircleMintGateway` never implements it) has no business living at the Application seam,
since placing it there would teach the production tier that mock mode exists. Its only callers are
`MockStablecoinGateway` and integration tests (which already reference Infrastructure via the
host). `ProviderDepositRecord` itself does stay in `Application/Ledger/Ports/` — it's part of the
real `IStablecoinGateway` interface contract, not mock-only.

### 7.8 Fallback for missed `externalEntities` decisions

PRD §11.4 point 4 — the same reconciliation umbrella also serves as the fallback for missed
`externalEntities` webhook decisions (polling pending registrations past a staleness threshold).
`DepositReconciliationPLan.md` scopes only the deposit half; the `externalEntities` staleness-poll
half is explicitly out of scope for that plan (see §7.9) and remains a documented gap, not
something this file should claim is built.

### 7.9 Explicitly out of scope (not built by this design)

Per `DepositReconciliationPLan.md`'s Global Constraints, these stay documented gaps (tracked in
`TODOS.md`, not silently dropped):

- Transfer/payout reconciliation (only deposits are covered).
- Stale-`PendingCompliance` polling for missed `externalEntities` decisions (§7.8's fallback is
  named in the PRD but not implemented by this plan).
- Amount/status divergence detection on deposits already recorded via the webhook path — only
  *missing* records are self-healed; a recorded-but-wrong-amount deposit is not detected.
- Notification-outbox integration for unresolvable mismatches — alerting is structured-log-only
  (no new alerting infrastructure); "alerts" in PRD §11.4 point 3 means a searchable log entry at
  `Error` level, not a paging/notification integration.

## 8. Tests required

| Layer | File | Covers |
|---|---|---|
| Unit | `TenantScopeResolverTests.cs` (see `01-tenancy-and-authorization.md`) | Not this file's concern — cross-referenced only. |
| Integration | `ProblemDetailsMappingTests.cs` | Each exception type maps to its documented HTTP status and `Type` URN; a validation failure round-trips through the real request pipeline. |
| Unit | `CircleClientOptionsTests.cs` | Options bind correctly from config; defaults match §4's spec. |
| Integration | (Task 1 wiring test) | The named `"Circle"` client resolves via `IHttpClientFactory` with base address from config. |
| Unit | `CircleErrorTranslatorTests.cs` | Parameterized over every row in §5.1's table — given a synchronous HTTP status + Circle `code`, or an async `errorCode`/`riskEvaluation`, asserts the correct PRD error class and exception type. |
| Unit | (Task 4 idempotency audit tests) | One test per money-moving gateway method (`CreateTransferAsync`, `RedeemAsync`, `GenerateDepositAddressAsync`, `RegisterRecipientAsync`, `CreateLinkedBankAccountAsync`) asserting the outbound JSON body's `idempotencyKey` equals the value passed into the gateway request DTO, not a value generated inside the gateway. |
| Unit | `MockProviderDepositLedgerTests.cs` | Seeded entries returned within the lookback window; excluded outside it; filtered by wallet. |
| Unit | `MockStablecoinGatewayTests.cs` | `ListRecentDepositsAsync` delegates to the mock ledger unchanged. |
| Unit | `DepositReconciliationServiceTests.cs` | No active sub-accounts is a no-op. Unmatched provider deposit is credited via `ProcessDepositCommandHandler` with the `reconciliation-{providerReferenceId}` correlation id. Already-recorded provider deposit is skipped (no double-credit). A handler failure for one deposit is logged and does not abort the pass. A gateway failure for one wallet does not abort reconciliation of the remaining wallets. |
| Integration | `SubAccountRepositoryTests.cs` | `ListActiveWithWalletAsync` excludes inactive, disabled, and walletless sub-accounts. |
| Integration | `DepositReconciliationIntegrationTests.cs` | End-to-end: seed a phantom provider deposit via `IMockProviderDepositLedger.SeedAsync`, run one `RunOnceAsync` pass directly (not the background service's timer, to stay deterministic), assert a real `Transaction` and `FundAccount` balance move. A second immediate pass over the same seeded deposit self-heals zero (no double-credit). |
| Retry/circuit-breaker behavior | (Task 1's Polly wiring, exercised indirectly) | Not separately listed as a distinct test file in source docs — covered by the gateway integration tests once Phase 3 lands real HTTP calls behind a fixture `HttpMessageHandler`; 4xx-other-than-429 must never trigger a retry (assert attempt count == 1), 5xx/429/timeout must retry up to `RetryCount` with backoff. |

## 9. Open corrections / decisions log

- **Exception type name correction:** all exceptions in §3.2 derive from `DomainException`, not
  `TreasuryDomainException` — a stale name from older docs, already corrected elsewhere this
  session; restated here for this file's own internal consistency.
- **Deposit-listing gateway placement (ADR 0006):** `ListRecentDepositsAsync` was originally
  drafted onto `ISubAccountGateway` in an earlier version of `DepositReconciliationPLan.md`; a
  docs-sync grill surfaced a three-way disagreement (shipped code had the interface in
  `Shared.Ports`, `Phase_1_Feature_Slices.md`'s table said `Compliance.Ports`, the reconciliation
  plan said `Ledger.Ports`) and ADR 0006 resolved it onto `IStablecoinGateway`
  (`Application.Ledger.Ports`). §7.3 above reflects the corrected placement; no open discrepancy.
- **Deposits-endpoint coverage gap (folded in from `DepositReconciliationPLan.md`'s own
  correction note):** an earlier version of the reconciliation plan hardcoded every self-healed
  record as `DepositSourceType.OnChain` and only listed the deposits endpoint — doubly wrong,
  since that endpoint returns only wire deposits and on-chain gaps were never actually covered.
  §7.2 reflects the corrected two-call design; no open discrepancy.
- **Circle error-code table verification (2026-07-17, two passes):** every code named in the task
  brief confirmed live, including `429` (follow-up pass, direct fetch of the error-codes reference
  page — §6). No mismatch found between the live page and the local mirror for anything fetched.
- **`idempotencyKey` request-body-field fact:** treated as settled per the task brief, not
  re-fetched live this pass; carried from the 2026-07-07 verification already on file in
  `docs/circle-mint-docs/reference/idempotent-requests.md`.
- **Folding `DepositReconciliationPLan.md` in — consistency check against Phase_1/Phase_3's
  error-handling assumptions:** no inconsistency found. The reconciliation job's per-item and
  per-wallet exception handling (§7.4) is a "catch, log, continue" pattern deliberately distinct
  from the RFC 7807 taxonomy in §3 — reconciliation runs as a background service with no HTTP
  caller to return a `ProblemDetails` response to, so it logs structurally instead of throwing a
  typed `DomainException` up to an exception handler that doesn't exist in that code path. This is
  not a taxonomy violation; it's the correct behavior for a poll loop versus a request pipeline,
  and `ProcessDepositCommandHandler` (the shared credit path) still throws the same typed
  exceptions (e.g. `ConflictException` if a sub-account isn't in a creditable state) that the
  webhook path would also see — reconciliation just catches them at its own boundary rather than
  letting them propagate to a non-existent HTTP response.
