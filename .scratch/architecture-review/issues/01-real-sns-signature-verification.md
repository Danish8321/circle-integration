Status: resolved
Severity: critical
Blocks: Production cutover

# Implement real AWS SNS webhook signature verification + hard env guard

`MockSnsSignatureVerifier.VerifyAsync` always returns `true` and is the only
`ISnsSignatureVerifier` implementation registered — wired unconditionally in
`CircleIntegrationServiceCollectionExtensions.cs:11`, outside any mock/dev/prod branch.

Circle webhooks (deposits, transfers, payouts, compliance decisions) drive real
money-moving state transitions via `/v1/webhooks/circle`. Any POST claiming to be
Circle is trusted today, in every environment including Production.

## Work

- Implement real AWS SNS message signature verification (cert fetch + chain validation
  + payload hash check per AWS SNS spec).
- Add a `MockModeGuard`-style hard environment check so the mock verifier structurally
  cannot be wired in Production (mirrors invariant 9's mock-mode guard pattern).
- Wire real verifier behind Development/mock toggle, matching existing gateway
  fake/live pairing convention.

## Comments

Resolved 2026-07-19: added `AwsSnsSignatureVerifier` (Infrastructure/Webhooks) implementing
§3.4 — cert-domain pattern check before fetch, canonical-string builder per message Type,
SHA1/SHA256 dual-version support, 24h in-memory cert cache. Extended `SnsEnvelope`/
`CircleSnsEnvelopeRequest` with the missing `Timestamp`/`SignatureVersion`/`Subject`/
`SubscribeURL`/`Token` fields the canonical string needs. DI (`CircleIntegrationServiceCollectionExtensions`)
now registers the mock verifier only when `mockModeEnabled` — same structural guard `MockModeGuard`
already gives invariant 9, since mock mode itself can't reach Production. 8 new unit tests
(self-signed cert, both types, both signature versions, tampered-message rejection, forged-host
rejection without fetch, non-HTTPS rejection, unsupported version rejection). No new NuGet
dependency — built on `System.Security.Cryptography` + `IMemoryCache`, both already available.
`test-fast.sh`: 411/411 passed.
