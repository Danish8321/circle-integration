Status: open

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

## Definition of done

- [ ] Human-portal-user audit header design decided (header name/shape) and implemented, or
      explicitly deferred with a recorded reason if portal auth itself isn't built yet.
- [ ] Stub-receiver bypass question answered — either add the exemption (with a test proving it,
      mirroring `WebhookDedupTests`' bypass assertion) or record why it's not needed.

## Comments
