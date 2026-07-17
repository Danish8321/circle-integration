using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TreasuryServiceOrchestrator.Api.Middleware;
using TreasuryServiceOrchestrator.Application.Exceptions;

namespace TreasuryServiceOrchestrator.UnitTests.Api;

public sealed class DomainExceptionHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DomainExceptionHandler _handler = new();

    [Fact]
    public async Task TenantForbiddenException_maps_to_403()
    {
        await AssertMappedAsync(new TenantForbiddenException(), StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task NotFoundException_maps_to_404()
    {
        await AssertMappedAsync(new NotFoundException("Thing not found."), StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ConflictException_maps_to_409()
    {
        await AssertMappedAsync(new ConflictException("State conflict."), StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task SubAccountAlreadyExistsException_maps_to_409()
    {
        await AssertMappedAsync(
            new SubAccountAlreadyExistsException("client-1"), StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task ProviderRejectedException_maps_to_422()
    {
        await AssertMappedAsync(
            new ProviderRejectedException("Provider rejected the request."),
            StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task ProviderUnavailableException_maps_to_503()
    {
        await AssertMappedAsync(
            new ProviderUnavailableException("Provider is unavailable."),
            StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Unknown_exception_returns_false()
    {
        var httpContext = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(
            httpContext, new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        handled.Should().BeFalse();
    }

    private async Task AssertMappedAsync(Exception exception, int expectedStatus)
    {
        var httpContext = CreateHttpContext();

        var handled = await _handler.TryHandleAsync(
            httpContext, exception, TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(expectedStatus);

        var problemDetails = await ReadProblemDetailsAsync(httpContext);
        problemDetails.Status.Should().Be(expectedStatus);
        problemDetails.Title.Should().NotBeNullOrWhiteSpace();
    }

    private static DefaultHttpContext CreateHttpContext() =>
        new() { Response = { Body = new MemoryStream() } };

    private static async Task<ProblemDetails> ReadProblemDetailsAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            httpContext.Response.Body,
            JsonOptions,
            TestContext.Current.CancellationToken);
        problemDetails.Should().NotBeNull();
        return problemDetails!;
    }
}
