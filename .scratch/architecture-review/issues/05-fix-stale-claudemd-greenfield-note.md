Status: resolved
Severity: low

# Fix stale "Greenfield: no code yet" line in .claude/CLAUDE.md

`.claude/CLAUDE.md` still opens with "Greenfield: no code yet" — codebase now
has 404 source files, 125 test files, 10 EF migrations across all four tiers.
Misleads future agents/reviewers into skipping code-level review.

## Work

- Remove/replace the stale greenfield framing with current state summary
  (tiers implemented, invariants enforced by fitness tests, link to latest
  architecture review).

## Comments
