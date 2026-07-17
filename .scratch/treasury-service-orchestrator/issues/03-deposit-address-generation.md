Status: open

Source: `docs/features/09-deposits-and-funding.md` (old source `docs/Phase_1_Feature_Slices.md`
Task 7, deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 02-mock-provider-gateway.

## Scope

Generate a permanent deposit address per (sub-account, chain, currency); list a sub-account's
addresses. Chain is a free-form string validated against a configured allow-list
(`SupportedChainsOptions`, default `["ETH"]`).

## Files (see Task 7 for exact list — module path corrected 2026-07-17: ADR 0001 places
`DepositAddress` under `Ledger`, not its own top-level namespace; `DepositAddresses` isn't
one of the five decided module names)

- New: `Domain/DepositAddress.cs`, `Application/Ledger/Ports/IDepositAddressRepository.cs`,
  `Application/Shared/SupportedChainsOptions.cs`,
  `Application/Ledger/DepositAddresses/{GenerateDepositAddressCommand,GenerateDepositAddressResult,
  GenerateDepositAddressCommandValidator,GenerateDepositAddressCommandHandler,
  ListDepositAddressesQuery,ListDepositAddressesQueryHandler}.cs`,
  `Infrastructure/Persistence/DepositAddressRepository.cs`,
  `Api/Ledger/DepositAddressesController.cs` (module-scoped path, matching the existing
  `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`).
- Modify: `Application/Ledger/Ports/{GatewayDtos.cs,IStablecoinGateway.cs}`,
  `CircleMintGateway`, `MockStablecoinGateway`, `DbContext`. (Corrected: `GenerateDepositAddressAsync`
  lives on `IStablecoinGateway`/Ledger, not `ISubAccountGateway`/Compliance — `DepositAddress` is
  a Ledger-module entity, ADR 0001; matches `Phase_1_Feature_Slices.md` Task 7.)
- Migration required — hand-review before apply.

## Key corrections that apply

- **Correction #9**: real Circle deposit-address endpoint requires a body `idempotencyKey`
  (UUID v4). This is NOT plain find-or-create — reserve a system-generated key in the existing
  idempotency table keyed by `(SubAccountId, Chain, Currency)`-derived scope before the gateway
  call, reuse on retry. `(SubAccountId, Chain, Currency)` unique index stays as local dedup.
- Correction #10: chain enum verified — default `["ETH"]` stands.
- Design-pass #6: gateway DTO is named `GeneratedDepositAddress` (distinct from the Application
  command result of the same base name, different namespace).
- Design-pass #5: `SupportedChainsOptions` wraps `List<string>`, doesn't inherit it — exposes
  `IsSupported(string chain)` only.

## Definition of done

- `GenerateDepositAddressCommandHandlerTests`, `ListDepositAddressesQueryHandlerTests` (Moq)
  green — cover idempotency-key reserve/reuse-on-retry path explicitly.
- `DepositAddressesEndpointsTests` integration green.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run (new endpoints).
- Migration hand-reviewed before `schema.sh apply`.

## Comments
