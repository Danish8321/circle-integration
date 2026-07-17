# Task 0: Solution scaffold hardening

Phase_1_Feature_Slices.md Task 1 presumes a working `TreasuryServiceOrchestrator.{Domain,Application,Infrastructure,Api}` solution with a `DbContext`, gateway ports, `ICallerContext`-adjacent cross-cutting wiring, and enforced layering already in place. The raw `dotnet new` scaffold committed to this repo (solution, CPM, `Directory.Build.props`, 4 src + 3 test projects) satisfies none of that yet — it is unmodified template output. Task 0 turns that template scaffold into the baseline every later task can build on. No feature logic, no entities, no endpoints beyond what's needed to prove the skeleton compiles and boots.

**Files:**
- Delete: `src/TreasuryServiceOrchestrator.Api/Controllers/WeatherForecastController.cs`
- Delete: `src/TreasuryServiceOrchestrator.Api/WeatherForecast.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/Program.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Persistence/TreasuryServiceOrchestratorDbContext.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICircleSubAccountGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICircleMintGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleSubAccountGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Infrastructure/Providers/Circle/CircleMintGateway.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/ICallerContext.cs`
- Create: `src/TreasuryServiceOrchestrator.Application/Shared/Ports/ITenantContext.cs`
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.json` (mock-mode flag placeholder)
- Modify: `src/TreasuryServiceOrchestrator.Api/appsettings.Development.json`
- Rename+modify: `tests/TreasuryServiceOrchestrator.ArchitectureTests/UnitTest1.cs` → `LayeringTests.cs`
- Delete placeholder: `tests/TreasuryServiceOrchestrator.UnitTests/UnitTest1.cs`
- Delete placeholder: `tests/TreasuryServiceOrchestrator.IntegrationTests/UnitTest1.cs`

**Interfaces:**
- Produces: `TreasuryServiceOrchestratorDbContext : DbContext` — empty (no `DbSet<T>` yet; Task 1+ add entities as they land).
- Produces: `ICircleSubAccountGateway`, `ICircleMintGateway` — empty marker-shaped port interfaces in `Application/Shared/Ports/` (no methods yet; each later task that needs a Circle call adds the method to the interface *and* the throwing stub in the same commit as it wires the real call, per vertical-slice discipline — Task 0 does not guess method shapes it doesn't yet need).
- Produces: `CircleSubAccountGateway`, `CircleMintGateway` — Infrastructure implementations constructed via `IHttpClientFactory`-created named/typed clients (`AddHttpClient<ICircleSubAccountGateway, CircleSubAccountGateway>()` in `Program.cs`), registered but empty until Task 1+ adds methods.
- Produces: `ICallerContext { string CallerId { get; } }`, `ITenantContext { string ClientCompanyId { get; } }` — empty port shapes; Task 1's `HttpCallerContext` fills these per its own Files/Interfaces list (Task 0 declares the seam, Task 1 fills it — no overlap).
- Produces: `TimeProvider` registered in DI (`builder.Services.AddSingleton(TimeProvider.System)`), consumed via constructor injection from Task 1 onward — never `DateTime.Now`/`UtcNow`.
- Produces: mock-mode startup guard — `if (builder.Environment.IsProduction() && builder.Configuration.GetValue<bool>("Circle:MockMode")) throw new InvalidOperationException(...)` in `Program.cs`, so mock mode is structurally impossible in Production regardless of config (rule 9). The mock gateway implementations themselves are Phase_1 Task 6's scope, not Task 0's — Task 0 only wires the guard that will apply to them.
- Produces: `LayeringTests` (NetArchTest.Rules) encoding this repo's `.claude/rules/*.md` tier table as executable assertions:
  - Domain must not have a dependency on Application, Infrastructure, Api, `Microsoft.EntityFrameworkCore`, or `Microsoft.AspNetCore.*`.
  - Application must not have a dependency on Infrastructure or Api.
  - Infrastructure must not be depended on by Domain or Application (i.e., no inward reference).
  - Api is the only project allowed to reference all three.
- Consumes (later tasks): every subsequent Phase_1 task's "Files" list assumes these paths/types already exist and compile; Task 1 is the first to add real methods to `ICallerContext`/gateway ports.

- [ ] **Step 1: Strip template cruft**
  - Delete `WeatherForecastController.cs`, `WeatherForecast.cs`.
  - Replace `Program.cs` body with: `AddControllers`, `AddOpenApi`, `AddSingleton(TimeProvider.System)`, the mock-mode Production guard, `MapControllers` — no other endpoints.

- [ ] **Step 2: Add empty `DbContext`**
  - `TreasuryServiceOrchestratorDbContext(DbContextOptions<TreasuryServiceOrchestratorDbContext> options) : DbContext(options)` — no `DbSet<T>` members yet.
  - Register in `Program.cs` via `AddDbContext<TreasuryServiceOrchestratorDbContext>(o => o.UseSqlServer(...))`, connection string from `appsettings.Development.json` pointing at LocalDB.

- [ ] **Step 3: Add gateway port stubs**
  - `ICircleSubAccountGateway`, `ICircleMintGateway` — empty interfaces (marker only).
  - `CircleSubAccountGateway`, `CircleMintGateway` — classes with an `HttpClient` constructor parameter (injected via typed client), no methods yet.
  - Wire both via `AddHttpClient<TInterface, TImpl>()` in `Program.cs`.

- [ ] **Step 4: Add cross-cutting port stubs**
  - `ICallerContext`, `ITenantContext` — interface shapes only, no implementation registered yet (Task 1 registers `HttpCallerContext`).

- [ ] **Step 5: Write `LayeringTests`**

```csharp
// tests/TreasuryServiceOrchestrator.ArchitectureTests/LayeringTests.cs
using NetArchTest.Rules;
using Xunit;

namespace TreasuryServiceOrchestrator.ArchitectureTests;

public class LayeringTests
{
    private const string Domain = "TreasuryServiceOrchestrator.Domain";
    private const string Application = "TreasuryServiceOrchestrator.Application";
    private const string Infrastructure = "TreasuryServiceOrchestrator.Infrastructure";

    [Fact]
    public void Domain_should_not_depend_on_outer_tiers_or_frameworks()
    {
        var result = Types.InAssembly(typeof(Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, "TreasuryServiceOrchestrator.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, "TreasuryServiceOrchestrator.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_should_not_be_referenced_by_domain_or_application()
    {
        var domainResult = Types.InAssembly(typeof(Domain.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOn(Infrastructure).GetResult();
        var applicationResult = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOn(Infrastructure).GetResult();

        Assert.True(domainResult.IsSuccessful);
        Assert.True(applicationResult.IsSuccessful);
    }
}
```

  - Requires an `AssemblyMarker` empty class per assembly (`Domain`, `Application`) purely so NetArchTest has a type to anchor `InAssembly(...)` on — add these as part of this step if not already present.

- [ ] **Step 6: Verify**
  - `dotnet build` — solution builds clean (warnings-as-errors).
  - `dotnet test` — `LayeringTests` pass; no other tests exist yet to break.
  - `dotnet run --project src/TreasuryServiceOrchestrator.Api` — boots without throwing, `/openapi/v1.json` resolves.

**Out of scope for Task 0** (deferred to the task that first needs it):
- Any entity, `DbSet<T>`, or migration (Task 1+).
- Gateway method bodies / mock implementations (Phase_1 Task 6).
- `ICallerContext`/`ITenantContext` implementations and DI registration (Phase_1 Task 1, Task 2).
- Idempotency-key middleware/pipeline (introduced by the first mutating-handler task that needs it).
