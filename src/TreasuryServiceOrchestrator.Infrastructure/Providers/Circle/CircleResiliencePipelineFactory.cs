using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace TreasuryServiceOrchestrator.Infrastructure.Providers.Circle;

/// <summary>
/// Builds the Polly resilience pipeline for the named "Circle" <see cref="System.Net.Http.HttpClient"/>
/// per docs/features/05-reliability-and-error-handling.md §4: per-attempt timeout, retry with
/// exponential backoff on 5xx/429/timeout/<see cref="HttpRequestException"/> only, and a circuit
/// breaker that opens after <see cref="CircleClientOptions.CircuitBreakerFailureThreshold"/>
/// consecutive failures for <see cref="CircleClientOptions.CircuitBreakerDurationOfBreak"/>.
/// </summary>
/// <remarks>
/// Wiring this handler onto the named "Circle" <see cref="System.Net.Http.HttpClient"/> registrations
/// in Program.cs is ticket 17.2 — this factory is intentionally unreferenced by Program.cs today.
/// Translating an open-circuit failure into <see cref="TreasuryServiceOrchestrator.Application.Exceptions.ProviderUnavailableException"/>
/// stays a gateway/error-translation concern (§5), not this pipeline's job.
/// </remarks>
public static class CircleResiliencePipelineFactory
{
    public const string PipelineName = "circle-resilience";

    /// <summary>
    /// Adds the Circle resilience pipeline (timeout, retry, circuit breaker) to an
    /// <see cref="IHttpClientBuilder"/>, resolving <see cref="CircleClientOptions"/> from DI.
    /// </summary>
    public static IHttpResiliencePipelineBuilder AddCircleResilienceHandler(this IHttpClientBuilder builder) =>
        builder.AddResilienceHandler(PipelineName, (pipelineBuilder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<CircleClientOptions>>().Value;
            ConfigurePipeline(pipelineBuilder, options);
        });

    /// <summary>
    /// Configures a resilience pipeline builder directly from an already-resolved
    /// <see cref="CircleClientOptions"/> instance — used by tests and by
    /// <see cref="AddCircleResilienceHandler"/> alike, so the policy shape is defined once.
    /// </summary>
    public static void ConfigurePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> pipelineBuilder,
        CircleClientOptions options)
    {
        pipelineBuilder
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(
                    Math.Max(options.CircuitBreakerDurationOfBreak.TotalSeconds * 2, 30)),
                BreakDuration = options.CircuitBreakerDurationOfBreak,
                ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome)),
            })
            .AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
    }

    /// <summary>
    /// True for §4's retryable set — 5xx, 429, timeout, <see cref="HttpRequestException"/> — and
    /// explicitly false for every other 4xx status (those are provider-rejected, not retryable).
    /// </summary>
    private static bool IsRetryable(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TimeoutRejectedException)
        {
            return true;
        }

        if (outcome.Exception is not null)
        {
            return false;
        }

        var statusCode = (int)outcome.Result!.StatusCode;
        return statusCode == StatusCodes.Status429TooManyRequests || statusCode >= 500;
    }

    private static class StatusCodes
    {
        public const int Status429TooManyRequests = 429;
    }
}
