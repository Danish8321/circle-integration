Status: resolved (unverified — no Docker)
Severity: medium
Blocked by: 01

# Add regression test: invalid webhook signature → 403

No test asserts invalid-signature rejection today, because the real verifier
(see issue 01) doesn't exist yet — only the always-true mock.

## Work

- Once real `ISnsSignatureVerifier` lands, add integration test hitting
  `/v1/webhooks/circle` with a tampered/invalid signature, assert 403 and no
  state mutation (no ledger/transaction/outbox writes).
- Add a positive-path test with a valid signature fixture too, if none exists.

## Comments

Added `WebhookSignatureVerificationTests.cs`: overrides `MockMode:Enabled=false` via
`WithWebHostBuilder` so the real `AwsSnsSignatureVerifier` is wired (mirrors prod DI
branching), POSTs with a forged `SigningCertURL` host, asserts 403 + no `WebhookInboxEntry`
row written. `dotnet build` on the IntegrationTests project: 0 errors. Could not run
`test-full.sh` — Docker is not running in this environment (all 72 integration tests fail
identically on Testcontainers container startup, not just the new one). Test is written and
compiles; needs a Docker-available run to confirm it actually passes before treating this as
fully verified.
