using FluentAssertions;
using NetArchTest.Rules;
using TreasuryServiceOrchestrator.Domain;
using TreasuryServiceOrchestrator.Infrastructure.Persistence;

namespace TreasuryServiceOrchestrator.ArchitectureTests;

public sealed class DependencyRuleTests
{
    private const string ApplicationNamespace = "TreasuryServiceOrchestrator.Application";
    private const string InfrastructureNamespace = "TreasuryServiceOrchestrator.Infrastructure";
    private const string ApiNamespace = "TreasuryServiceOrchestrator.Api";

    [Fact]
    public void Domain_must_not_depend_on_outer_tiers()
    {
        var result = Types.InAssembly(typeof(Money).Assembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Domain_must_not_depend_on_framework_or_io_types()
    {
        var result = Types.InAssembly(typeof(Money).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "System.IO")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_must_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(ICommandHandler<,>).Assembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Application_must_not_depend_on_entity_framework()
    {
        var result = Types.InAssembly(typeof(ICommandHandler<,>).Assembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_must_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(TreasuryServiceOrchestratorDbContext).Assembly)
            .Should()
            .NotHaveDependencyOn(ApiNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FailureMessage(result));
    }

    [Fact]
    public void No_project_uses_module_or_use_case_folder_namespaces()
    {
        // ADR 0001: module/use-case sub-namespaces (Compliance/Ledger/Webhooks/Admin) were
        // removed in favor of flat by-kind folders. This guards against the axis creeping back.
        string[] retiredModuleNamespaces =
        [
            $"{ApplicationNamespace}.Compliance",
            $"{ApplicationNamespace}.Ledger",
            $"{ApplicationNamespace}.Webhooks",
            $"{ApplicationNamespace}.Admin",
            $"{ApiNamespace}.Compliance",
            $"{ApiNamespace}.Ledger",
            $"{ApiNamespace}.Webhooks",
            $"{ApiNamespace}.Admin",
        ];

        var offendingTypes = Types.InAssembly(typeof(ICommandHandler<,>).Assembly)
            .That()
            .ResideInNamespaceStartingWith(retiredModuleNamespaces[0])
            .GetTypes()
            .ToList();

        foreach (var ns in retiredModuleNamespaces)
        {
            offendingTypes.AddRange(Types.InAssembly(typeof(ICommandHandler<,>).Assembly)
                .That()
                .ResideInNamespaceStartingWith(ns)
                .GetTypes());
        }

        offendingTypes.Should().BeEmpty();
    }

    private static string FailureMessage(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null
            ? "Dependency rule violated."
            : $"Dependency rule violated by: {string.Join(", ", result.FailingTypeNames)}";
}
