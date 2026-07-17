Status: open

Source: `docs/features/02-mock-mode.md` (old source `docs/Phase_1_Feature_Slices.md` Task 6,
deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 01-webhook-pipeline-core.

## Scope

Mock `ISubAccountGateway`/`IStablecoinGateway` implementations + a simulated webhook emitter
that schedules deliveries through Task 5's real inbox pipeline. Structural
Production-mode guard is mandatory (CLAUDE.md invariant 9 / Global Constraints) — not just a
config flag.

## Files (see Task 6 for exact list)

- New: `Infrastructure/Mocks/{MockProviderOptions,MockModeGuard,IMockRandomSource,
  ScheduledMockWebhook,IMockWebhookScheduler,MockWebhookChannel,MockWebhookDispatcher,
  MockWebhookDispatchBackgroundService,MockSubAccountGateway,MockStablecoinGateway}.cs`.
- Modify: `Program.cs`, `appsettings.json`, `appsettings.Development.json`.

## Key corrections that apply

- `EntityRegistrationStatusMapper.Map` expects `"Pending"/"Accepted"/"Rejected"`
  (case-insensitive) — not the old stub's uppercase literals.
- Correction #1 (recipient status literals): mock gateway/emitter must use real Circle literals
  (`pending_verification` on create; webhook `active`/`denied`).
- Correction #2 (transfer `running` intermediate event + `failed` outcome must be simulated).
- `MockModeGuard.Validate(mockModeEnabled, environmentName)` throws if enabled in
  `Environments.Production` — this is the structural guard, test it explicitly.

## Definition of done

- `MockModeGuardTests`: throws in Production, no-ops elsewhere — Moq not needed here (pure
  logic), but keep same xUnit v3 conventions.
- `MockWebhookDispatcherTests`, `MockSubAccountGatewayTests`, `MockStablecoinGatewayTests` green.
- `MockProviderWiringTests` integration test green — confirms DI picks mock gateways only when
  configured, never silently in Production.
- `check.sh`, `test-fast.sh`, `test-full.sh` green.

## Comments
