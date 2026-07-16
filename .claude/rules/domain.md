---
paths:
  - "src/TreasuryServiceOrchestrator.Domain/**"
---

# Domain tier — must not

- Must not reference Application, Infrastructure, or Api projects.
- Must not reference EF Core, ASP.NET Core, or any framework/IO type (no `DbContext`,
  no `HttpClient`, no `System.IO`, no serialization attributes).
- Must not call `DateTime.Now`/`DateTime.UtcNow` directly — inject `TimeProvider`.
- Must not represent money as anything but `Money(decimal Amount, string CurrencyCode)` —
  no floating-point money.
