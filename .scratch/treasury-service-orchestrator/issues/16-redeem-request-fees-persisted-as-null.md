Status: resolved

Source: found 2026-07-18 while implementing ticket 10 (`10-demo-script-e2e.md`) — driving a real
redemption settle through the mock payouts webhook surfaced a structural persistence bug in
already-shipped code, not a test-authoring problem. Blocks ticket 10 from going fully green (the
redemption-completion step of the demo script cannot assert `Fees`/`NetAmount` are visible).

Blocks: 10-demo-script-e2e.

## Scope

`RedeemRequest.Settle(fees, netAmount, nowUtc)`
(`src/TreasuryServiceOrchestrator.Domain/RedeemRequest.cs`) sets `Fees`/`NetAmount` to real,
non-null `Money` instances (confirmed `fees = Money(0m, "USDC")` on the mock's zero-fee payout
path). `ProcessPayoutStatusCommandHandler` calls `Settle` then persists via
`LedgerPostingService.PostAsync` → `IUnitOfWork.SaveChangesAsync`. After that save, re-reading the
row directly (raw SQL against `RedeemRequests.FeesAmount`) shows `FeesAmount = NULL` while
`FeesCurrencyCode = 'USDC'` (i.e. a **partial** null — the complex object's reference is clearly
non-null since the sibling column wrote correctly, but the `Amount` scalar specifically writes
NULL).

`TreasuryServiceOrchestratorDbContext.ConfigureRedeemRequestFeesAndNetAmount` maps `Fees`/
`NetAmount` as EF Core 10 optional (`IsRequired(false)`) `ComplexProperty`s, with
`.HasSentinel(decimal.MinValue)` on the `Amount` scalar — added specifically to stop a genuine
zero fee from being treated as "unset" (per that method's own comment, the CLR-default-0m
heuristic was the original suspected cause). Confirmed via `db.Model.FindEntityType(...)` at
runtime: `sentinel = -79228162514264337593543950335` (i.e. `decimal.MinValue`, applied correctly)
and `Amount.IsNullable = false` (it is the complex type's presence-marker property, since
`CurrencyCode` has no `IsRequired()` call and so is itself nullable). Despite the sentinel being
configured and the real value (`0m`) not equal to it, `FeesAmount` still persists as `NULL`. The
sentinel fix, as configured, does not resolve the underlying null-on-write behavior — needs
either a different EF Core 10 optional-complex-property configuration, or a design change (e.g.
make `Fees`/`NetAmount` non-optional `Money` on `RedeemRequest`, defaulting to `Money.Zero(...)`
before `Settle`, removing the optional-complex-type edge case entirely).

Not confirmed whether this also affects `NetAmount` (not yet isolated in a clean run — the raw
per-column dump this ticket is based on only captured `Fees`; re-run the same diagnostic query
against `NetAmount`/`NetCurrencyCode` before starting the fix to confirm scope).

## Note on file state

`TreasuryServiceOrchestratorDbContext.cs`'s `ConfigureRedeemRequestFeesAndNetAmount`
(`HasSentinel` + explanatory comment) and this session's own diagnostic `Assert.Fail` scaffolding
in `DemoScriptEndToEndTests.cs` were found already present/being edited in the working tree
during this session, not authored by this agent turn — indicating another process/agent may
already be investigating this exact defect concurrently. Coordinate before starting the fix to
avoid duplicate/conflicting work; check `TreasuryServiceOrchestratorDbContext.cs` and
`DemoScriptEndToEndTests.cs` for uncommitted changes first.

## Definition of done

- `RedeemRequest.Fees`/`NetAmount` round-trip through EF Core (create with a genuine `$0` fee,
  save, reload in a fresh `DbContext`/scope) with `Amount` and `CurrencyCode` both correctly
  persisted and read back non-null.
- Root-caused: document why `HasSentinel(decimal.MinValue)` did not prevent the null write (EF
  Core 10 optional-complex-property semantics differ from what the current comment assumes), not
  just patched around it.
- Diagnostic scaffolding removed from `DemoScriptEndToEndTests.cs` (the `Assert.Fail(...)`/raw-SQL
  debug blocks in `CreateAndCompleteRedemptionAsync`) once the real assertions
  (`Assert.NotNull(Fees)`, `Assert.Equal(0m, Fees.Amount)`, `Assert.NotNull(NetAmount)`,
  `Assert.Equal(50m, NetAmount.Amount)`) pass for real.
- A focused unit/integration test (Infrastructure-tier, real `DbContext`) proving a `$0` fee
  survives a save/reload cycle, independent of the full demo-script test.
- `check.sh` clean, `test-fast.sh`/`test-full.sh` green including `DemoScriptEndToEndTests`.

## Comments

Root cause confirmed 2026-07-18: `HasSentinel` on a table-split optional `ComplexProperty`'s
scalar sub-property does not prevent EF Core 10 from writing NULL for a value equal to the
sub-property's *CLR default* (`0m` for `decimal`) — configuring an explicit non-zero sentinel
(`decimal.MinValue`) had no effect; confirmed empirically (a non-zero fee persisted, a zero fee
did not, with the same sentinel configured both times) and via runtime model introspection
(sentinel really was `decimal.MinValue` at write time). `CurrencyCode` persisted fine because it
has no CLR-default collision (`null` vs `"USDC"`), which is why only `Amount` nulled out — this
is a per-property, not per-object, quirk of the table-splitting mapping.

Fixed by switching `Fees`/`NetAmount` from table-split columns to `.ToJson()` complex-property
mapping (EF Core 10 feature) — the whole `Money` object now serializes as one JSON document per
column (`FeesJson`, `NetAmountJson`), so "unset" is a single JSON `NULL` and a real `$0` fee is an
ordinary populated document; no per-property zero-vs-unset ambiguity is possible. Migration
`20260718045823_RedeemRequestFeesToJson` drops `FeesAmount`/`FeesCurrencyCode`/`NetAmount`/
`NetCurrencyCode`, adds `FeesJson`/`NetAmountJson` — reviewed before applying, a real
representation change (not a disguised rename), no live data at risk (ticket 07/09 columns never
released). New focused test: `RedeemRequestFeesPersistenceTests.ZeroFeeAndNetAmount_Survive...`
(Infrastructure-tier, real `DbContext`, fresh scope on reload). Diagnostic scaffolding removed
from `DemoScriptEndToEndTests.cs`. `test-full.sh`: 53/53 green.
