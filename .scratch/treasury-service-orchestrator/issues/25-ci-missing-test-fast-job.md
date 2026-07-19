Status: open

# CI pipeline never runs test-fast.sh

`.github/workflows/ci.yml` has 3 jobs: `check`, `contract`, `test-full`. None
invoke `.claude/scripts/test-fast.sh`. Consequence: `UnitTests` project (403
tests) and `ArchitectureTests` project (6 NetArchTest rules encoding ADR 0001
dependency-rule invariants) get zero CI coverage — only caught if someone runs
`test-fast.sh` locally before pushing. A broken architecture rule or unit
regression can merge to `main` clean.

## Fix

Add a `test-fast` job to `ci.yml`, parallel to `check` (or same job, extra
step), running `sh .claude/scripts/test-fast.sh` before `contract`/`test-full`
gate on it.

## Comments

Found during CI pipeline verification, 2026-07-19. User said leave it for now.
