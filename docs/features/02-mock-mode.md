# Feature: Mock Mode (cross-cutting)

Source: `docs/PRD.md` §13, `docs/Phase_1_Feature_Slices.md` Task 6, `docs/adr/0007-mock-emits-real-provider-shapes.md`, `docs/adr/0006-deposit-listing-on-stablecoin-gateway.md`, `docs/Phase_3_Circle_Integration_Plan.md` Task 9 / Global Constraints.

## 1. Scope / PRD requirement

A **configuration-switched simulated provider inside the API** (PRD §13, original requirement
item 12) so consumer teams and the Phase 1 demo (PRD §15.1) can exercise full money-movement
flows end to end without a Circle sandbox dependency. When enabled, every provider call is
served by a deterministic simulator instead of Circle:

- **Deterministic screening** — entity submissions are accepted or rejected based on a
  recognizable input pattern (a magic business-name suffix triggers `Rejected`), so test cases
  are repeatable, not randomized.
- **Simulated webhooks** — the simulator emits the same event shapes
  (`externalEntities`, `deposits`, `transfers`, `payouts`, `addressBookRecipients`) through the
  **real** webhook-processing pipeline (inbox → dedup → per-topic processor), with a configurable
  realistic delay. Mock mode is not a shortcut around the webhook pipeline — it is a producer
  that feeds it.
- **Failure injection** — configurable provider 5xx / `ProviderUnavailableException` scenarios.
- **Latency injection** — configurable added response latency, for resilience/timeout testing.
- **Production guard** — mock mode is structurally impossible to enable in the `Production`
  environment: a hard environment check at host startup, not configuration alone (CLAUDE.md
  invariant 9; PRD §13).

Consumer teams point integration environments at a mock-mode deployment; Circle's real sandbox
remains available separately for pre-production verification. Phase 1's entire end-to-end demo
(PRD §15.1, slices 1–8) runs on mock mode with zero Circle dependency.

## 2. Gateway split mock mode must honor

Mock mode swaps two ports, each owned by a different module (corrected 2026-07-17, see ADR 0006 —
some older passages in `Phase_1_Feature_Slices.md` predate this split and should not be used as
the reference):

| Port | Module | Owns | Circle impl | Mock impl |
|---|---|---|---|---|
| `ISubAccountGateway` | `Application.Compliance.Ports` | Entity/registration ops only: `CreateExternalEntityAsync`, `GetExternalEntityAsync` | `CircleSubAccountGateway` | `MockSubAccountGateway` |
| `IStablecoinGateway` | `Application.Ledger.Ports` | All money-moving ops: `RedeemAsync`, `GetTransferStatusAsync`, `GenerateDepositAddressAsync`, `RegisterRecipientAsync`, `ListRecentDepositsAsync`, `GetMainWalletBalanceAsync` | `CircleMintGateway` | `MockStablecoinGateway` |

`ISubAccountGateway` must **not** grow deposit-address, recipient, or balance methods — those are
money-moving and belong on `IStablecoinGateway` (ADR 0006's rationale: compliance vs. ledger
module boundary, not just "who happens to expose it in the Circle API"). Current shipped code
(`src/TreasuryServiceOrchestrator.Application/Compliance/Ports/ISubAccountGateway.cs`) already
matches this — only `CreateExternalEntityAsync` is present.

## 3. Design

### 3.1 `MockProviderOptions`

Bound from config section `"MockProvider"`, `public` (deliberately — unlike `Circle`-namespace
types, it must bind from `IConfiguration` in `Program.cs`):

```csharp
public sealed class MockProviderOptions
{
    public bool Enabled { get; set; }
    public int WebhookDelayMilliseconds { get; set; } = 500;
    public int ResponseLatencyMilliseconds { get; set; }
    public double FailureInjectionRate { get; set; }
    public string RejectBusinessNameSuffix { get; set; } = "REJECTME";
}
```

Defaults: `appsettings.json` base sets `MockProvider:Enabled = false` (safe in every environment
including any future overlay that forgets to override it explicitly). `appsettings.Development.json`
sets `Enabled = true` (Phase 1 has no real Circle credentials, so local/dev runs default to mock).

### 3.2 Production guard — `MockModeGuard`

```csharp
public static class MockModeGuard
{
    public static void Validate(bool mockModeEnabled, string environmentName)
    {
        if (mockModeEnabled && string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MockProvider:Enabled cannot be true when the environment is Production. " +
                "Mock mode is structurally disallowed in production (PRD §13).");
        }
    }
}
```

Called **unconditionally** at host startup in `Program.cs` (not only when `Enabled` is true) —
so a misconfigured `Production` deployment with `MockProvider:Enabled=true` fails host startup
immediately instead of silently running with mock providers. This is the single mechanism the
"structurally impossible" claim rests on: it is an environment-name check independent of any
other config flag, so no combination of config values alone can enable mock mode in Production.

`Phase_3_Circle_Integration_Plan.md`'s Global Constraints reaffirm this for the real-integration
phase: `MockModeGuard` (Phase 1 Task 6) stays the single source of truth for "is mock mode
allowed here" — the Phase 3 production cutover (its Task 11) **verifies** the guard fires against
the real deployed `Environments.Production` value, it never bypasses or replaces it. Phase 3's
Task 9 (mTLS) was decided **skip** (US-only entity, no MiCA exposure) and is unrelated to this
guard — it does not weaken or interact with the mock-mode check.

### 3.3 `MockSubAccountGateway` (implements `ISubAccountGateway`)

```csharp
public sealed class MockSubAccountGateway(
    IOptions<MockProviderOptions> options,
    IMockWebhookScheduler webhookScheduler,
    IMockRandomSource randomSource) : ISubAccountGateway
{
    // ConcurrentDictionary<string walletId, string complianceState> — in-memory state,
    // must survive across requests, so this gateway is a singleton (see 3.6).

    Task<CreateExternalEntityResult> CreateExternalEntityAsync(...)
        // 1. optional simulated latency (ResponseLatencyMilliseconds)
        // 2. optional simulated failure (FailureInjectionRate via randomSource.NextDouble())
        //    -> throws ProviderUnavailableException
        // 3. generates a walletId, decides Accepted/Rejected by business-name suffix match
        //    against RejectBusinessNameSuffix (case-insensitive), records state
        // 4. schedules an "externalEntities" webhook carrying the terminal state
        //    (ADR 0007 shape, see 3.5) via IMockWebhookScheduler
        // 5. returns immediately with ComplianceState = "Pending" (async provider semantics
        //    preserved: the caller only learns the terminal state via the webhook)

    Task<ExternalEntityStatusResult> GetExternalEntityAsync(walletId, ct)
        // returns the state recorded at creation time (defaults to "Pending" if unknown)
}
```

`ComplianceState` values are `"Pending"` / `"Accepted"` / `"Rejected"` (case-insensitive) — the
mock must emit these, not an older stub's uppercase `"ACCEPTED"`/`"REJECTED"`, because
`EntityRegistrationStatusMapper.Map(string)` requires this casing.

### 3.4 `MockStablecoinGateway` (implements `IStablecoinGateway`)

Same latency/failure-injection preamble as 3.3, then serves each money-moving op deterministically
in-memory (e.g. `RedeemAsync` returns `TransferStatus.Complete` immediately with the fiat amount
converted 1:1 into the target currency's `Money`; `GetTransferStatusAsync` replays the recorded
status). `GenerateDepositAddressAsync`, `RegisterRecipientAsync`, `ListRecentDepositsAsync`, and
`GetMainWalletBalanceAsync` follow the same pattern — deterministic in-memory state plus, where
the real Circle flow is webhook-driven (deposits, recipient approval), a scheduled simulated
webhook rather than a synchronous terminal result. Holds in-memory state (`ConcurrentDictionary`),
so it is also registered as a singleton.

### 3.5 Simulated webhook emitter — real Circle shapes (ADR 0007)

The mock emitter is not a separate payload format from the real webhook pipeline; it is the same
pipeline's producer. Per ADR 0007:

- Payloads scheduled by the mock gateways are the same **notification envelope** Circle sends:
  `{ clientId, notificationType, version, <resource>: {...} }`, where `<resource>` uses Circle's
  real field names (e.g. `externalEntity.walletId`, `externalEntity.complianceState`).
- Money-bearing resources (deposits, transfers, payouts) carry money as nested objects with
  **string** amounts: `{ "amount": "1000.00", "currency": "USD" }` — never a bare `decimal`
  field, never an invented `netAmount`/`sourceType` field that doesn't exist on the real payload.
- In production, deliveries arrive as an Amazon SNS `Notification` whose `Message` field is this
  envelope as a JSON string; the inbox unwraps `Message` before a per-topic processor deserializes
  it. The mock path skips the SNS transport wrapper (there is no real SNS in mock mode) but
  produces the **same unwrapped envelope** the processor expects, so processors do not know or
  care whether the JSON came from SNS or the mock scheduler.
- This makes mock-mode tests double as contract tests for real Circle payload parsing — the
  Phase 3 sandbox cutover should surface only field-level drift, never a payload-shape rewrite.

Emitter mechanics (`IMockWebhookScheduler` / `MockWebhookChannel` / `MockWebhookDispatcher` /
`MockWebhookDispatchBackgroundService`):

```csharp
public sealed record ScheduledMockWebhook(string Topic, string PayloadJson, TimeSpan Delay);

public interface IMockWebhookScheduler
{
    void Schedule(ScheduledMockWebhook webhook);
}
```

- `MockWebhookChannel` is an unbounded `System.Threading.Channels.Channel<ScheduledMockWebhook>`
  implementing `IMockWebhookScheduler.Schedule` as a non-blocking write.
- `MockWebhookDispatcher.DispatchOneAsync(ct)` reads one scheduled webhook, honors its `Delay`,
  opens a DI scope, and calls the **real** `WebhookProcessor.HandleAsync(IncomingWebhookEvent(topic,
  "mock-{guid}", payloadJson), ct)` — the same entry point real SNS deliveries use after inbox
  unwrap. This is the mechanism that makes "driven through the real webhook pipeline" literally
  true: there is no separate mock-only processing path.
- `MockWebhookDispatchBackgroundService` is a `BackgroundService` that loops
  `DispatchOneAsync` for the life of the host.
- Any future mock-emitting task (deposits, transfers, payouts, addressBookRecipients — Phase 1
  Tasks 8–11) only needs to inject `IMockWebhookScheduler` and call `Schedule`; no dispatcher or
  wiring changes are needed per new topic.

### 3.6 DI wiring — conditional gateway registration

```csharp
var mockProviderOptions = builder.Configuration.GetSection("MockProvider").Get<MockProviderOptions>()
    ?? new MockProviderOptions();
MockModeGuard.Validate(mockProviderOptions.Enabled, builder.Environment.EnvironmentName);
builder.Services.Configure<MockProviderOptions>(builder.Configuration.GetSection("MockProvider"));

if (mockProviderOptions.Enabled)
{
    builder.Services.AddSingleton<IMockRandomSource, SystemRandomSource>();
    builder.Services.AddSingleton<MockWebhookChannel>();
    builder.Services.AddSingleton<IMockWebhookScheduler>(sp => sp.GetRequiredService<MockWebhookChannel>());
    builder.Services.AddSingleton<MockWebhookDispatcher>();
    builder.Services.AddHostedService<MockWebhookDispatchBackgroundService>();
    builder.Services.AddSingleton<IStablecoinGateway, MockStablecoinGateway>();
    builder.Services.AddSingleton<ISubAccountGateway, MockSubAccountGateway>();
}
else
{
    builder.Services.AddScoped<IStablecoinGateway, CircleMintGateway>();
    builder.Services.AddScoped<ISubAccountGateway, CircleSubAccountGateway>();
}
```

Mock gateways are **singletons** (they hold `ConcurrentDictionary` state that must survive across
requests within a process); Circle gateways are **scoped** (stateless HTTP-backed). This is a
deliberate lifetime asymmetry, not an oversight — do not "fix" it to match.

`MockModeGuard.Validate` runs before the `if`, unconditionally, so it fires regardless of which
branch is about to be taken.

## 4. Tests required

| Layer | File | Covers |
|---|---|---|
| Unit | `MockModeGuardTests.cs` | Throws in `Production` when enabled; does not throw in `Development`/`Sandbox`/`Staging` when enabled; does not throw in `Production` when disabled. |
| Unit | `MockWebhookDispatcherTests.cs` | A scheduled webhook is delivered to the real `WebhookProcessor` → correct `IWebhookTopicProcessor` receives the exact `PayloadJson`. |
| Unit | `MockSubAccountGatewayTests.cs` | Normal business name → `Pending` return + scheduled `Accepted` webhook containing the wallet id. Magic-suffix name → scheduled `Rejected` webhook. `FailureInjectionRate = 1.0` → throws `ProviderUnavailableException`. `GetExternalEntityAsync` replays the state recorded at creation. |
| Unit | `MockStablecoinGatewayTests.cs` | `RedeemAsync` returns `Complete` with fiat amount in the target currency. `FailureInjectionRate = 1.0` → throws `ProviderUnavailableException`. `GetTransferStatusAsync` replays the status recorded at redemption. |
| Integration | `MockProviderWiringTests.cs` | `MockProvider:Enabled=true` + `Development` → DI resolves `MockStablecoinGateway`/`MockSubAccountGateway`. `MockProvider:Enabled=true` + `Production` → host startup (`factory.Services`) throws `InvalidOperationException`. |

Failure/latency injection is tested via `IMockRandomSource`/`IMockWebhookScheduler` test doubles
(`FixedRandomSource`, `CapturingScheduler`) rather than real `Random`/real delays, so the tests
stay deterministic and fast.

## 5. Open corrections / decisions log

- **Gateway split correction (this session, ADR 0006):** `Phase_1_Feature_Slices.md` Task 6's
  interface listing and some of its prose predate the Compliance/Ledger gateway split — treat
  the table in §2 above (and `ISubAccountGateway.cs` as shipped) as the corrected reference, not
  the older passages. No open discrepancy remains; this file already reflects the corrected
  state, cross-checked against shipped code.
- **Webhook payload shape (ADR 0007):** confirmed — the mock emitter payload shape described in
  §3.5 (unwrapped Circle envelope, string-amount money objects) matches ADR 0007's decision.
  `Phase_1_Feature_Slices.md` Tasks 5–11's inline payload DTO snippets are superseded wherever
  they show a flatter invented shape; this file does not restate those snippets, only the
  corrected shape.
- **mTLS (Phase 3 Task 9) vs. mock mode:** confirmed no interaction — mTLS was decided **skip**
  (US-only entity, no MiCA exposure); `MockModeGuard` remains the sole gate independent of that
  decision. No discrepancy found.
- No other discrepancies found between PRD §13, Phase_1 Task 6, and the two ADRs during this
  pass.
