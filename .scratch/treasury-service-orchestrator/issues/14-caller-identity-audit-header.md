Status: resolved

Source: `docs/features/01-tenancy-and-authorization.md` (open item, line ~40); `docs/README.md`
§7 (item "02.2 / CallerIdentityMiddleware").
Blocked by: none.

## Scope

Two related `CallerIdentityMiddleware` gaps:

1. **PRD §2.2's "human portal user audit header" is still unimplemented.** The middleware
   resolves `ClientCompanyId` for machine callers (Legacy Applications) but has no mechanism yet
   for an APISO Portal Admin's human identity to be captured/audited on requests they make
   through the portal — PRD §2.2 calls for this to be distinguishable in audit records.
2. **Whether `09`'s internal-notifications stub receiver needs the same
   `/v1/webhooks/circle`-style bypass-path treatment is still open.** The middleware already has
   a path-based bypass exempting `/v1/webhooks/circle` from `ClientCompanyId` scoping (PRD §10
   item 7, resolved per `01-webhook-pipeline-core.md`). Not yet decided whether
   `InternalNotificationsStubController`'s endpoint (an internal-to-internal call, not
   tenant-scoped) needs an equivalent exemption or whether it's out of `CallerIdentityMiddleware`'s
   path entirely (e.g. never routed through the public API surface).

## Decisions resolved during grilling (2026-07-17)

1. **Human-portal-user audit header: deferred, not designed now.** No portal/client exists in this
   repo (API-only per CLAUDE.md — no Angular/TS client) and no portal authentication mechanism
   exists to source a human identity from. Designing a header shape now would be speculative with
   no consumer to validate it against. Re-open when a portal auth client is actually built.
2. **Stub-receiver bypass: answered — yes, needed, and already resolved by ticket 09.**
   `docs/features/13-internal-notifications-outbox.md` §6.1 adds `/internal/notifications` as the
   first `CallerIdentityMiddleware.BypassPaths` entry (the dispatcher POSTs with no
   `ClientCompanyId` header at all, so it needs the same exemption shape as `/v1/webhooks/circle`).
   No separate work needed here — ticket 09 owns the implementation.

## Definition of done

- [x] Human-portal-user audit header — explicitly deferred, reason recorded above.
- [x] Stub-receiver bypass question answered — yes, implemented as part of ticket 09's
      `BypassPaths` addition (`docs/features/13-internal-notifications-outbox.md` §6.1).

## Comments
