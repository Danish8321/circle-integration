Status: open

Source: `docs/features/10-outbound-transfers-and-recipients.md` (old source
`docs/Phase_1_Feature_Slices.md` Task 9, deleted 2026-07-17 — superseded by the per-feature doc
restructure).
Blocked by: 04-ledger-transaction-and-balance.

## Scope

Register/list/get recipients + `addressBookRecipients` webhook-driven decision processing.

## Files (see Task 9 for exact list)

- New: `Domain/{RecipientStatus,Recipient}.cs`,
  `Application/Ledger/Ports/IRecipientRepository.cs`,
  `Application/Ledger/Recipients/{RegisterRecipientCommand,RegisterRecipientResult,
  RegisterRecipientCommandValidator,RegisterRecipientCommandHandler,ListRecipientsQuery,
  ListRecipientsQueryHandler,GetRecipientQuery,GetRecipientQueryHandler,
  RecipientStatusMapper,ProcessRecipientDecisionCommand,ProcessRecipientDecisionResult,
  ProcessRecipientDecisionHandler}.cs` (module path corrected 2026-07-17: ADR 0001 places
  `Recipient` under `Ledger`, not a flat top-level `Recipients` namespace),
  `Application/Webhooks/AddressBookRecipientsWebhookTopicProcessor.cs`,
  `Infrastructure/Persistence/RecipientRepository.cs`,
  `Api/Ledger/RecipientsController.cs` (module-scoped path, matching the existing
  `Api/Compliance/SubAccountsController.cs` convention — not `Api/Controllers/`; corrected 2026-07-17).
- Modify: `Application/Ledger/Ports/{GatewayDtos.cs,IStablecoinGateway.cs}`,
  `CircleMintGateway`, `MockStablecoinGateway`, `DbContext`, `Program.cs`. (Corrected:
  `RegisterRecipientAsync` lives on `IStablecoinGateway`/Ledger, not `ISubAccountGateway`/
  Compliance — `Recipient` is a Ledger-module entity, ADR 0001; matches
  `Phase_1_Feature_Slices.md` Task 9.)

## Key corrections that apply

- **Correction #1**: `RecipientStatusMapper` must not throw on unknown literals. REST enum:
  `pending_verification | verification_succeeded | active`; webhook vocabulary:
  `pending | inactive | active | denied`. Map `active` → `Active`, `denied` → `Denied`,
  anything else → `PendingApproval` (log it). `pending_approval` is NOT a real literal.
- Correction #7: webhook payload is the real SNS envelope shape, not an invented flat DTO.

## Definition of done

- `RegisterRecipientCommandHandlerTests`, `ProcessRecipientDecisionHandlerTests` (Moq) —
  explicitly test the unknown-literal-doesn't-throw branch.
- `AddressBookRecipientsWebhookTopicProcessorTests` green.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run.
- Migration hand-reviewed before `schema.sh apply`.

## Comments
