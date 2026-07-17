# ADR 0006: Deposit listing lives on IStablecoinGateway, not ISubAccountGateway

**Status:** Accepted (2026-07-17, resolved via grilling)

## Decision

`ListRecentDepositsAsync` (deposit reconciliation's read of provider-side deposits) is a method
on `IStablecoinGateway` (Ledger module), implemented by `CircleMintGateway`/`MockStablecoinGateway`
— not on `ISubAccountGateway` (Compliance module, `CircleSubAccountGateway`/`MockSubAccountGateway`).

## Rationale

`CONTEXT.md`'s existing gateway split (`ISubAccountGateway` = entity/registration/recipient
ops, `IStablecoinGateway` = money-moving ops: transfers, redemptions, status) already answers
this — deposits are money-moving, not compliance. `docs/DepositReconciliationPLan.md` had wired
`ListRecentDepositsAsync` onto `ISubAccountGateway` instead, found during a docs-sync grill
(surfaced alongside three-way disagreement on `ISubAccountGateway`'s module: code had it in
`Shared.Ports`, `Phase_1_Feature_Slices.md`'s table said `Compliance.Ports`, the reconciliation
plan said `Ledger.Ports`). Splitting by module and routing deposit-listing to the correct
gateway costs standing up `IStablecoinGateway`/`CircleMintGateway`/`MockStablecoinGateway` now
instead of reusing existing SubAccount gateway classes — but avoids baking a Compliance/Ledger
boundary violation into the first Ledger-module feature to land.

## Consequences

`docs/DepositReconciliationPLan.md` Task 2 changes from "add method to `ISubAccountGateway`" to
"create `IStablecoinGateway` (Ledger.Ports) with `ListRecentDepositsAsync` as its first method,
plus `CircleMintGateway`/`MockStablecoinGateway` implementations." `ISubAccountGateway` stays at
`Application.Compliance.Ports` (matching `Phase_1_Feature_Slices.md`'s table; confirmed already
correct in shipped code as of the sub-account endpoints rework — no move needed).

Also fixed 2026-07-17: `Phase_1_Feature_Slices.md`'s own Module Boundaries table had
`IStablecoinGateway` listed under `Shared`, contradicting this ADR's Ledger placement. Corrected
inline in that doc.
