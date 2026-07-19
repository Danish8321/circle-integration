using System.Net;

using FluentAssertions;
using Polly;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Providers.Circle;

/// <summary>
/// Ticket 17.3: exercises <see cref="CircleResiliencePipelineFactory.ConfigurePipeline"/> directly
/// against a fixture <see cref="HttpMessageHandler"/> — retry/circuit-breaker behavior per
/// docs/features/05-reliability-and-error-handling.md §4, without a running Circle sandbox.
/// </summary>
public sealed class CircleResiliencePipelineTests
{
    [Fact]
    public async Task NonRetryable4xx_IsNotRetried()
    {
        var handler = new CountingStubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var pipeline = BuildPipeline(new CircleClientOptions { RetryCount = 3, CircuitBreakerFailureThreshold = 100 });

        var response = await Execute(pipeline, httpClient, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ServerError_IsRetriedUpToRetryCountThenFails()
    {
        var handler = new CountingStubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var options = new CircleClientOptions { RetryCount = 3, CircuitBreakerFailureThreshold = 100 };
        var pipeline = BuildPipeline(options);

        var response = await Execute(pipeline, httpClient, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.InvocationCount.Should().Be(options.RetryCount + 1);
    }

    [Fact]
    public async Task TooManyRequests429_IsRetriedLikeServerError()
    {
        var handler = new CountingStubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var options = new CircleClientOptions { RetryCount = 3, CircuitBreakerFailureThreshold = 100 };
        var pipeline = BuildPipeline(options);

        var response = await Execute(pipeline, httpClient, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.InvocationCount.Should().Be(options.RetryCount + 1);
    }

    [Fact]
    public async Task AfterFailureThreshold_CircuitOpensAndFailsFastWithoutReachingInnerHandler()
    {
        var handler = new CountingStubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.circle.test/") };
        var options = new CircleClientOptions
        {
            RetryCount = 1,
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerDurationOfBreak = TimeSpan.FromSeconds(30),
        };
        var pipeline = BuildPipeline(options);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Drive enough failing calls through to trip the breaker (each call makes RetryCount + 1 attempts).
        while (handler.InvocationCount < options.CircuitBreakerFailureThreshold)
        {
            try
            {
                await Execute(pipeline, httpClient, cancellationToken);
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                break;
            }
        }

        var invocationsBeforeProbe = handler.InvocationCount;

        var act = async () => await Execute(pipeline, httpClient, cancellationToken);

        await act.Should().ThrowAsync<Polly.CircuitBreaker.BrokenCircuitException>();
        handler.InvocationCount.Should().Be(invocationsBeforeProbe);
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(CircleClientOptions options)
    {
        // Collapse only sub-timeout delays (retry backoff). The per-attempt timeout and the
        // circuit-breaker's sampling/break windows stay on the real clock — harmless, because
        // every call in these tests completes in-memory in microseconds and never approaches them.
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>
        {
            TimeProvider = new CollapseShortDelaysTimeProvider(TimeSpan.FromSeconds(options.TimeoutSeconds)),
        };
        CircleResiliencePipelineFactory.ConfigurePipeline(builder, options);
        return builder.Build();
    }

    private static Task<HttpResponseMessage> Execute(
        ResiliencePipeline<HttpResponseMessage> pipeline, HttpClient httpClient, CancellationToken cancellationToken) =>
        pipeline.ExecuteAsync(
            static async (state, ct) =>
                await state.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, "ping"), ct),
            (HttpClient: httpClient, CancellationToken: cancellationToken),
            cancellationToken).AsTask();

    /// <summary>
    /// Fires only <em>short, positive</em> Polly timers immediately — i.e. retry backoff delays
    /// (sub-second to a few seconds) — so these tests don't sleep for real backoff.
    /// <para>
    /// Two classes of timer are left untouched, and collapsing either is the ticket-21 flake:
    /// <list type="bullet">
    ///   <item>A <see cref="Timeout.InfiniteTimeSpan"/> (-1ms) due-time is a <em>disarmed</em>
    ///   timer that Polly arms later via <see cref="ITimer.Change"/> (the per-attempt timeout is
    ///   created this way). Forcing it to <see cref="TimeSpan.Zero"/> fires a dormant timer at once
    ///   — cancelling the in-flight attempt and throwing a spurious
    ///   <c>TimeoutRejectedException</c>.</item>
    ///   <item>Any real delay at or above <paramref name="collapseBelow"/> (the per-attempt
    ///   timeout, the circuit-breaker sampling/break windows) stays on the real clock — harmless,
    ///   since every call here completes in-memory in microseconds and never reaches it.</item>
    /// </list>
    /// </para>
    /// </summary>
    private sealed class CollapseShortDelaysTimeProvider(TimeSpan collapseBelow) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var collapse = dueTime > TimeSpan.Zero && dueTime < collapseBelow;
            return base.CreateTimer(callback, state, collapse ? TimeSpan.Zero : dueTime, period);
        }
    }

    private sealed class CountingStubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private int _invocationCount;

        public int InvocationCount => _invocationCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return await responder(request, cancellationToken);
        }
    }
}
