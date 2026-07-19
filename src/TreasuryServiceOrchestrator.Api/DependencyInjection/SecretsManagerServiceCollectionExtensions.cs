using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace TreasuryServiceOrchestrator.Api.DependencyInjection;

public static class SecretsManagerServiceCollectionExtensions
{
    // ADR 0009: Secrets Manager is a Production-only concern — local dev keeps using
    // appsettings.Development.json/user-secrets, same environment-check pattern as
    // CLAUDE.md invariant 9's mock-mode guard.
    public static async Task<WebApplicationBuilder> AddProductionSecretsAsync(this WebApplicationBuilder builder)
    {
        var options = new SecretsManagerOptions();
        builder.Configuration.GetSection(SecretsManagerOptions.SectionName).Bind(options);

        if (!options.Enabled)
        {
            return builder;
        }

        // Hard environment check once opted in, mirroring MockModeGuard's shape but inverted:
        // this guards against SecretsManager:Enabled=true being set outside Production (e.g. a
        // dev laptop with stray config), not against it being disabled inside Production.
        if (!builder.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{SecretsManagerOptions.SectionName}:{nameof(SecretsManagerOptions.Enabled)} is true but "
                    + $"ASPNETCORE_ENVIRONMENT is '{builder.Environment.EnvironmentName}', not Production.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretId))
        {
            throw new InvalidOperationException(
                $"{SecretsManagerOptions.SectionName}:{nameof(SecretsManagerOptions.SecretId)} is required when "
                    + $"{SecretsManagerOptions.SectionName}:{nameof(SecretsManagerOptions.Enabled)} is true.");
        }

        using var client = options.Region is { Length: > 0 } region
            ? new AmazonSecretsManagerClient(Amazon.RegionEndpoint.GetBySystemName(region))
            : new AmazonSecretsManagerClient();

        var response = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = options.SecretId });

        var secretValues = JsonSerializer.Deserialize<Dictionary<string, string?>>(response.SecretString)
            ?? throw new InvalidOperationException($"Secret '{options.SecretId}' did not contain a JSON object.");

        builder.Configuration.AddInMemoryCollection(secretValues);

        return builder;
    }
}
