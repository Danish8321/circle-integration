Status: resolved
Severity: low

# Align SubAccount.SetDisabled with guarded-transition convention

`SubAccount.SetDisabled(bool)` is a plain setter — inconsistent with the rest
of the entity's style, which uses guarded named-transition methods (e.g.
factory methods + invariant checks elsewhere in Domain).

## Work

- Replace with named transitions, e.g. `Disable()` / `Enable()`, each
  validating current state before transition, matching sibling entities'
  pattern.
- Update call sites in handlers.

## Comments
