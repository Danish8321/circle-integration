Status: resolved
Severity: medium

# Wire AWS Secrets Manager per ADR 0009

ADR 0009 commits to AWS Secrets Manager for Circle API credentials. No SDK
reference or wiring exists yet — currently Phase-3 scope, but should be tracked
so it doesn't slip past when real Circle credentials get issued (same risk
pattern as the webhook signature stub).

## Work

- Add AWS Secrets Manager SDK reference to Infrastructure.
- Replace config-based Circle API key loading with Secrets Manager fetch.
- Confirm no real Circle keys ever land in `appsettings*.json` (audit before close).

## Comments

Resolved 2026-07-19. ADR 0009 named a config-provider package (`Amazon.Extensions.
Configuration.SecretsManager`) that doesn't exist on NuGet — asked user, went with official
`AWSSDK.SecretsManager` (not the third-party `Kralizek.Extensions.Configuration.
AWSSecretsManager` community wrapper) to keep credential-loading dependencies AWS-maintained.

Added `SecretsManagerOptions` (`SecretsManager` config section: `Enabled`/`SecretId`/`Region`)
and `AddProductionSecretsAsync` (Api/DependencyInjection), called first thing in `Program.cs`.
Guard is an explicit `Enabled` opt-in (mirrors `MockProviderOptions:Enabled`), not environment
alone — environment-only gating broke `contract.sh`'s OpenAPI doc generation, since that tool
runs `Program` with `ASPNETCORE_ENVIRONMENT` unset (defaults to Production). Once `Enabled=true`,
a hard check requires `ASPNETCORE_ENVIRONMENT=Production` or it throws — same
inverted-`MockModeGuard` shape, catches `Enabled=true` stray config outside real Production.
Secret is assumed (undocumented by ADR) to be a flat JSON object of dotted config-path keys,
merged via `AddInMemoryCollection`; local dev/tests untouched (`Enabled` defaults `false`).

`test-fast.sh`: 411/411 passed. `contract.sh`: OpenAPI doc regenerated in this same change
(new `SnsEnvelope` fields from ticket 01 show up correctly; unrelated pre-existing tag-order
diff noise). ArchitectureTests build clean (no dependency-rule violations).
