Status: open

Source: `docs/features/10-outbound-transfers-and-recipients.md` (old source
`docs/Phase_1_Feature_Slices.md` Task 10, deleted 2026-07-17 — superseded by the per-feature doc
restructure).
Blocked by: 04-ledger-transaction-and-balance.

## Scope

Create/list/get outbound transfers + `transfers` webhook processing. Also owns the on-chain
deposit branch deferred from 04-ledger-transaction-and-balance (correction #5): the `transfers`
topic processor branches on direction — incoming to one of our wallets = deposit credit via
`ProcessDepositCommand`, outgoing = transfer status update.

## Files (see Task 10 for exact list)

- New: `Domain/Transfer.cs`, `Application/Ledger/Ports/ITransferRepository.cs`,
  `Application/Ledger/Transfers/{CreateTransferCommand,CreateTransferCommandValidator,
  CreateTransferCommandHandler,ListTransfersQuery,ListTransfersQueryHandler,GetTransferQuery,
  GetTransferQueryHandler,ProcessTransferStatusCommand,
  ProcessTransferStatusCommandHandler}.cs` (module path corrected 2026-07-17: ADR 0001
  places `Transfer` under `Ledger`, not a flat top-level `Transfers` namespace),
  `Infrastructure/Webhooks/TransfersWebhookTopicProcessor.cs`,
  `Infrastructure/Persistence/TransferRepository.cs`, `Api/Ledger/TransfersController.cs`
  (module-scoped path, matching the existing `Api/Compliance/SubAccountsController.cs`
  convention — not `Api/Controllers/`; corrected 2026-07-17).
- Modify: `IStablecoinGateway` (add `CreateTransferAsync`), `GatewayDtos`
  (`CreateTransferGatewayRequest`/`Result`), `CircleMintGateway`, `MockStablecoinGateway`,
  `DbContext`, `Program.cs`.

## Key corrections that apply

- **CLAUDE.md invariant 12 / Global Constraint**: `POST /v1/businessAccount/transfers` carries
  **no** Travel Rule originator name/address fields — do not add them to
  `CreateTransferCommand`/gateway request.
- Correction #2: mock emitter must simulate the `running` intermediate transfer-webhook event
  and support a `failed` outcome (mapper already handles `running` → `Pending`).
- Correction #4: destination shape is `{ type: "verified_blockchain", addressId: <recipient
  UUID> }` — `CreateTransferGatewayRequest.DestinationRecipientId` becomes `addressId`.
- Correction #5: this ticket owns the incoming-branch of the `transfers` topic processor
  (on-chain deposit credit), not just outgoing transfer status.
- Design-pass #2: reuse 04's ledger-posting module for the transfer debit — don't reimplement.
- Design-pass #1: `Money`, not `decimal`, for every balance touch here.

## Definition of done

- `CreateTransferCommandHandlerTests`, `ListTransfersQueryHandlerTests`,
  `GetTransferQueryHandlerTests`, `ProcessTransferStatusCommandHandlerTests` (Moq).
- `TransfersWebhookTopicProcessorTests` — cover both incoming (deposit) and outgoing (transfer
  status) branches, plus the `running` intermediate state.
- `MockStablecoinGatewayTransferTests` green.
- `TransfersEndpointsTests` integration green.
- `check.sh`, `test-fast.sh`, `test-full.sh` green; `contract.sh` re-run.

## Comments
