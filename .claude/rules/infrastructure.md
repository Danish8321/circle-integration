---
paths:
  - "src/TreasuryServiceOrchestrator.Infrastructure/**"
---

# Infrastructure tier — must not

- Must not contain business/use-case logic — only port implementations (repositories,
  `CircleSubAccountGateway`, `CircleMintGateway`, `DbContext`, EF configs/migrations).
- Must not introduce a generic `IRepository<T>` abstraction over `DbContext` — repositories are
  use-case-shaped, matching the Application port they implement.
- Must not use `new HttpClient()` for provider calls — use `IHttpClientFactory`.
- Must not be referenced by Domain or Application (dependency points inward only).
- Must not enable mock-mode gateways via config alone — hard environment check required.
