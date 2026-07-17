# ADR 0007: Mock webhook emitter emits real Circle notification shapes

**Status:** Accepted (2026-07-17, resolved via grilling)

## Decision

Webhook payload contracts across the pipeline — inbox, per-topic processors, and the mock
emitter — use Circle's **real** notification shapes, not invented flat DTOs:

- Deliveries are an Amazon SNS `Notification` whose `Message` field is a string containing
  Circle's JSON envelope: `{ clientId, notificationType, version, <resource>: {...} }`.
- Money values are nested objects with **string** amounts: `{ "amount": "1000.00", "currency": "USD" }`.
- The inbox unwraps the SNS `Message`; processors deserialize the real envelope + resource shapes.
- The mock emitter (Phase 1, PRD §13) produces byte-shape-identical payloads on the real topics
  (`deposits` fiat-only; on-chain deposits on `transfers`; `wire` for bank-account verification).

## Rationale

Phase 3's core claim is "the durable inbox → dedup → per-topic-processor pipeline is reused
unchanged; only transport and signature verification are swapped." That claim is only true if
Phase 1's processors already parse what Circle actually sends. Invented shapes (flat
`CircleDepositWebhookPayload` with `decimal Amount`, a `sourceType` discriminator that doesn't
exist, `netAmount` instead of the optional `toAmount`) would make the Phase 1 demo and every
pipeline test prove the wrong contract, then force a Phase 3 translation layer plus a re-test of
the whole pipeline against new shapes — the expensive path, discovered only at sandbox time.

Alternative considered: keep invented shapes in Phase 1, add an adapter in Phase 3. Rejected:
the adapter's mapping surface is exactly the payload-shape knowledge we'd be deferring, and the
Phase 1 E2E demo (PRD §15.1) would validate a contract no real message ever has.

## Consequences

- `Phase_1_Feature_Slices.md` payload DTO snippets (Tasks 5–11) are superseded where they invent
  shapes — see that file's "Corrections from doc-grilling 2026-07-17" items 5–7.
- Mock-mode tests double as contract tests for Circle payload parsing; Phase 3 Task 10's sandbox
  run should surface only field-level drift, not shape rework.
- Parsing handles string decimal amounts at the edge and converts to `Money` before anything
  crosses into Application/Domain (Global Constraint: `Money` is the only monetary type at that
  boundary).
