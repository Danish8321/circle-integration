---
paths:
  - "src/TreasuryServiceOrchestrator.Application/**"
---

# Application tier — must not

- Must not reference Infrastructure or Api projects.
- Must not know about SQL Server, `DbContext`, or any concrete EF type — only ports
  (interfaces) under `Ports/`.
- Must not use `new HttpClient()` — provider gateways are Infrastructure's job; Application
  only defines the port (`IStablecoinGateway`, `ISubAccountGateway`, etc.).
- Must not use module or use-case folders (`Compliance`, `Ledger`, `Webhooks`, `Admin` — that
  axis was removed, see `docs/adr/0001-module-boundaries.md`). File by kind only:
  `Handlers/`, `Ports/`, `Dtos/`, `Validators/`, `Services/`, `Exceptions/`.
- Must not skip `CancellationToken ct = default` on any `HandleAsync`.
- Must not read tenant id from a route/body parameter — only from `ICallerContext`.
- Must not let mock-mode gateways be reachable when `ASPNETCORE_ENVIRONMENT=Production`.
