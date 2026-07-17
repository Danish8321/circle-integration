# ADR 0005: Provider-agnostic naming across the Domain/Application boundary

**Status:** Accepted (2026-07-16, resolved via grilling)

## Decision

Identifiers that cross the Domain/Application boundary use provider-agnostic names — e.g. `ProviderWalletId`, not `CircleWalletId`. The literal provider name ("Circle") is confined to the Infrastructure tier: type names like `CircleSubAccountGateway`, `CircleMintGateway`, and the `Infrastructure/Circle/` namespace.

## Rationale

PRD Goal 3 (§1.2): "Provider abstraction. Adding a new provider requires no change to the consumer-facing API contract." Existing implementation named a Domain-adjacent field `CircleWalletId` (e.g. `ISubAccountRepository.GetByCircleWalletIdAsync`), which leaks the provider name past Infrastructure — a second provider (Fireblocks, Bridge — PRD §15.4 roadmap) would either force a rename that touches Domain/Application, or force awkward dual-naming (`CircleWalletId` populated by a non-Circle gateway). Naming it provider-agnostically now costs a rename; naming it later costs a rename plus every caller that assumed "Circle" was permanent.

## Consequences

Rename `CircleWalletId` (and any other Circle-prefixed identifier living in Domain/Application) to a provider-agnostic equivalent (`ProviderWalletId`) as part of the next task that touches it — not a standalone sweep, but flag it whenever a task in `Phase_1_Feature_Slices.md` modifies a file carrying the old name. Infrastructure-tier type names (`CircleSubAccountGateway`, `CircleMintGateway`) are unaffected — the provider name there is not just acceptable but useful for identifying the concrete implementation.
