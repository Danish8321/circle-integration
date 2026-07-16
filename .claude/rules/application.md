---
paths:
  - "src/TreasuryServiceOrchestrator.Application/**"
---

# Application tier — must not

- Must not reference Infrastructure or Api projects.
- Must not know about SQL Server, `DbContext`, or any concrete EF type — only ports
  (interfaces) under `<Module>/Ports/`.
- Must not use `new HttpClient()` — provider gateways are Infrastructure's job; Application
  only defines the port (`IStablecoinGateway`, `ISubAccountGateway`, etc.).
- Must not place use-case code in a flat shared `Services/` folder — one folder per use case
  under its module (`Compliance`, `Ledger`, `Webhooks`, `Admin`, `Shared` — B0.5).
- Must not skip `CancellationToken ct = default` on any `HandleAsync`.
- Must not read tenant id from a route/body parameter — only from `ICallerContext`.
- Must not let mock-mode gateways be reachable when `ASPNETCORE_ENVIRONMENT=Production`.
