---
paths:
  - "src/TreasuryServiceOrchestrator.Api/**"
---

# Api tier — must not

- Must not put business logic in a controller — dispatch to a handler, return its result.
- Must not return a Domain (or EF) entity directly from an endpoint — map to an Application DTO.
- Must not skip a validation filter on any endpoint.
- Must not catch domain exceptions in a controller — RFC 7807 `ProblemDetails` is the only
  error contract, produced centrally.
- Must not read `ClientCompanyId`/tenant scope from anywhere but the validated caller
  credential/middleware — never trust a route or body value directly.
- Must not add a client project (Angular/TS/etc.) — this repo is API-only.
