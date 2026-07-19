Status: resolved 2026-07-18. Fix in `CircleResiliencePipelineTests.cs` (test-only). See ## Resolution.
Priority: P0 — TOP. Preempts every other open ticket. Red test in the suite = verification
contract broken; nothing else ships green until this is resolved.

Type: bug

Source: found 2026-07-18 by the `stop.sh` post-run test gate during an architecture review.
`test-fast.sh` reports **401 passed, 1 failed, 402 total**. The single red test is
`CircleResiliencePipelineTests.AfterFailureThreshold_CircuitOpensAndFailsFastWithoutReachingInnerHandler`.
This is a **test-infrastructure defect**, not a product defect — the resilience pipeline itself
(`CircleResiliencePipelineFactory`) is correct; the test's fake `TimeProvider` is wrong.

## Symptom

```
Polly.Timeout.TimeoutRejectedException : The operation didn't complete within the allowed timeout of '00:00:10'.
---- System.OperationCanceledException : The operation was canceled.
   at TreasuryServiceOrchestrator.UnitTests.Infrastructure.Providers.Circle.CircleResiliencePipelineTests
      .AfterFailureThreshold_CircuitOpensAndFailsFastWithoutReachingInnerHandler()
      ...CircleResiliencePipelineTests.cs:line 80
```

Line 80 is the `await Execute(...)` inside the drive-the-breaker loop (not the final probe
assertion). The test expects to drive N failing calls to trip the breaker, then assert the next
call throws `BrokenCircuitException` fast. Instead a `TimeoutRejectedException` escapes the loop
(it is not caught — only `BrokenCircuitException` is), failing the test before the probe.

## Root cause

`CircleResiliencePipelineTests.InstantTimeProvider` (tests file, lines 115-119):

```csharp
private sealed class InstantTimeProvider : TimeProvider
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        base.CreateTimer(callback, state, TimeSpan.Zero, period);   // <-- collapses EVERY timer to fire immediately
}
```

The intent (per its own doc comment) is to make retry backoff + circuit-breaker sampling timers
fire immediately so the test doesn't sleep for real backoff. But the override is **indiscriminate**:
it forces `dueTime = TimeSpan.Zero` on *every* timer the pipeline creates — including the
**per-attempt timeout** timer from `.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds))`
(`CircleResiliencePipelineFactory.cs:63`).

Pipeline nesting is `Retry( CircuitBreaker( Timeout( handler ) ) )` (order added =
outer→inner; confirmed by the stack trace: Timeout inside CircuitBreaker inside Retry). With the
timeout timer's due-time collapsed to zero, the 10s per-attempt timeout races the in-memory
`CountingStubHttpMessageHandler.SendAsync`. Whichever wins is nondeterministic:

- handler wins → returns 500 → retryable → normal flow (test would pass)
- timeout timer wins → `TimeoutRejectedException` + `OperationCanceledException` → escapes the
  loop's `catch (BrokenCircuitException)` → **test fails**

Hence flaky: passes some runs, fails others. This run it failed.

## Fix options (pick one, smallest first)

1. **Scope the fake clock to retry/sampling only, leave the timeout real.** The timeout is 10s and
   the stub handler is synchronous/in-memory (microseconds) — it never needs to be collapsed. Make
   `InstantTimeProvider` collapse only timers whose `dueTime` is below a threshold (backoff/sampling
   are sub-second-to-seconds; the timeout is 10s), OR
2. **Drop the custom `TimeProvider` and shrink the config instead** — set `TimeoutSeconds` large,
   `RetryCount`/backoff to values whose real delay is negligible with `UseJitter`+small base, and
   let real time pass (tests already run in <1s each). Removes the racy fake entirely, OR
3. **Advance logical time explicitly.** Use a controllable `FakeTimeProvider`
   (`Microsoft.Extensions.TimeProvider.Testing`) and call `Advance(...)` between calls so backoff
   and sampling elapse deterministically while the timeout stays at its real 10s budget. Cleanest;
   adds a test-only package if not already referenced.

Prefer (1) or (3). (2) risks reintroducing real sleeps.

## Acceptance criteria

- `test-fast.sh` is green: 402/402, and the resilience test is deterministic across ≥20
  consecutive runs (no `TimeoutRejectedException` leakage).
- The other three tests in the file (`NonRetryable4xx_IsNotRetried`,
  `ServerError_IsRetriedUpToRetryCountThenFails`, `TooManyRequests429_IsRetriedLikeServerError`)
  stay green and still avoid real backoff sleeps (suite stays under the 60s `test-fast.sh` budget).
- No change to `CircleResiliencePipelineFactory` (product code) — the pipeline shape is correct;
  this is a test-only fix. If a product change turns out to be needed, re-triage: that would be a
  different, higher-stakes ticket.

## Resolution — root cause was misdiagnosed above

Instrumenting the fake `TimeProvider.CreateTimer` (logging every `dueTime`) showed the real
mechanism, which differs from the "timeout timer collapsed to zero races the handler" theory in
## Root cause:

- Polly arms the **per-attempt timeout as a disarmed timer**: it calls `CreateTimer` with
  `dueTime = Timeout.InfiniteTimeSpan` (`-00:00:00.001`), then arms it later via `ITimer.Change`.
- The old `InstantTimeProvider` forced `dueTime = TimeSpan.Zero` on **every** timer — including
  that `-1ms` disarmed one. Collapsing an infinite/disarmed due-time to zero **fires a dormant
  timer immediately**, cancelling the in-flight attempt and throwing the spurious
  `TimeoutRejectedException`. Retry backoff timers (positive, ~2/4/8s) were the only ones that
  legitimately needed collapsing.
- So the flake is "collapse of the infinite due-time," not a magnitude race. Fix option (1) as
  originally worded (threshold on magnitude) is **insufficient** — `-1ms < anyThreshold` is true,
  so a naive `dueTime < threshold` still collapses the disarmed timeout. Verified: that exact
  intermediate attempt reintroduced the failure (4/25 runs, now as `InvocationCount` off-by-one).

### Fix applied (test-only, no product change)

`InstantTimeProvider` replaced by `CollapseShortDelaysTimeProvider(collapseBelow)`, built with
`collapseBelow = options.TimeoutSeconds`. It collapses a timer **only** when
`dueTime > TimeSpan.Zero && dueTime < collapseBelow` — i.e. positive, finite, sub-timeout backoff
delays. Infinite/disarmed (`-1ms`) timers and any real delay ≥ timeout (per-attempt timeout, CB
sampling/break windows) keep their real due-time; they never fire because every stubbed call
completes in-memory in microseconds.

### Evidence

- `test-fast.sh`: **402/402**, 352ms.
- Resilience test in isolation: **0 failures / 40** consecutive runs.
- Full 402-suite (the threadpool-load condition that surfaced the flake): **0 failures / 4**
  consecutive runs, each <0.7s. Well under the 60s budget.
- `CircleResiliencePipelineFactory` (product) untouched.

## Notes

- Product code confirmed correct on read: retry handles 5xx/429/timeout/`HttpRequestException`
  only (`IsRetryable`), circuit breaker `FailureRatio=1.0`, `MinimumThroughput=threshold`,
  `SamplingDuration=max(break*2, 30s)`, per-attempt timeout innermost. No defect there.
- Belongs to the ticket-17 resilience effort (`CircleResiliencePipelineFactory` doc references
  17.2/17.3) but is a newly-surfaced flake, filed separately per one-file-per-ticket convention.
