Status: open

Source: `docs/features/08-banking-and-wire-instructions.md`,
`docs/features/11-redemption-and-payouts.md` (old source `docs/Phase_1_Feature_Slices.md`
Task 11, deleted 2026-07-17 — superseded by the per-feature doc restructure).
Blocked by: 06-outbound-transfers.

## Scope

Rework `RedeemRequest` to carry gross/fees/net separately (flat fee only known once the payout
webhook lands). Adds `LinkedBankAccount` as Redemption's destination account, with async
verification arriving on the `wire` topic. Uses `POST /v1/businessAccount/payouts` — not the
Travel-Rule-gated `POST /v1/payouts` — no originator fields here either.

## Files (see Task 11 for exact list)

- Modify: `Domain/RedeemRequest.cs`.
- New: `Domain/{LinkedBankAccount,LinkedBankAccountStatus}.cs`,
  `Application/Ledger/Ports/ILinkedBankAccountRepository.cs`,
  `Application/Ledger/LinkedBankAccounts/{CreateLinkedBankAccountCommand,
  CreateLinkedBankAccountCommandValidator,CreateLinkedBankAccountCommandHandler,
  ListLinkedBankAccountsQuery,GetLinkedBankAccountQuery}.cs`,
  `Application/Ledger/Redemptions/{CreateRedemptionCommand,CreateRedemptionCommandValidator,
  CreateRedemptionCommandHandler,ListRedemptionsQuery,GetRedemptionQuery,
  ProcessPayoutStatusCommand,ProcessPayoutStatusCommandHandler}.cs` (module paths corrected
  2026-07-17: ADR 0001 places `LinkedBankAccount`/`Redemption` under `Ledger`, not flat
  top-level namespaces),
  `Infrastructure/Webhooks/PayoutsWebhookTopicProcessor.cs`,
  `Infrastructure/Persistence/LinkedBankAccountRepository.cs`,
  `Api/Ledger/LinkedBankAccountsController.cs` (module-scoped path, matching the existing
  `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`; corrected 2026-07-17).
- Delete: `Application/Redeem/{CreateRedeemCommand,CreateRedeemCommandHandler,
  CreateRedeemValidator,CreateRedeemResult}.cs`.
- Rewrite: `Infrastructure/Persistence/RedeemRequestRepository.cs`.
- Modify: `IRedeemRequestRepository`, `GatewayDtos`, `IStablecoinGateway`, `CircleMintGateway`,
  `MockProviderOptions`, `MockStablecoinGateway`, `DbContext`, `Program.cs`.

## Key corrections that apply

- **Correction #3**: real payout field is `toAmount` (not `netAmount`), and it's optional.
  Webhook DTO: nullable `ToAmount`; net = `toAmount` when present else computed
  `amount − fees`. `ProcessPayoutStatusCommand` keeps a non-nullable net computed at the
  mapping edge.
- **Correction #6**: `wire` topic missing — linked-bank-account verification is async
  (`pending → complete | failed`), not synchronous at create. Add the `wire` topic processor;
  mock gateway returns `pending` on create, emitter schedules `complete`/`failed` `wire`
  webhook.
- Design-pass #2: reuse the shared ledger-posting module for the payout debit — gross amount
  debited on `Pending`, fees/net recorded only on completion (mirrors Task 10's transfer flow).
- CLAUDE.md invariant 12: no Travel Rule originator fields on this endpoint either.

## Definition of done

- `CreateLinkedBankAccountCommandHandlerTests`, `CreateRedemptionCommandHandlerTests`,
  `ProcessPayoutStatusCommandHandlerTests` (Moq) — explicit test for `toAmount` absent
  (computed net) and present (verbatim net) branches.
- `PayoutsWebhookTopicProcessorTests`, and a new `wire`-topic processor test — pending/complete/
  failed transitions.
- Api integration tests for `LinkedBankAccountsController` + redemption endpoints.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run.
- Migration hand-reviewed (RedeemRequest column changes are a rework, not a drop/add — verify
  the generated migration expresses gross/fees/net as additive columns, not data loss).

## Comments
