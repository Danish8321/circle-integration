using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using TreasuryServiceOrchestrator.Infrastructure.Webhooks;

namespace TreasuryServiceOrchestrator.UnitTests.Infrastructure.Webhooks;

public sealed class AwsSnsSignatureVerifierTests : IDisposable
{
    private const string CertUrl = "https://sns.us-east-1.amazonaws.com/SimpleNotificationService-abc123.pem";

    private readonly X509Certificate2 signingCertificate;
    private readonly RSA privateKey;
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly Mock<IHttpClientFactory> httpClientFactory = new(MockBehavior.Strict);

    public AwsSnsSignatureVerifierTests()
    {
        privateKey = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=sns.us-east-1.amazonaws.com", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        signingCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    public void Dispose()
    {
        privateKey.Dispose();
        signingCertificate.Dispose();
        cache.Dispose();
    }

    private AwsSnsSignatureVerifier CreateVerifier()
    {
        cache.Set(new Uri(CertUrl), signingCertificate, TimeSpan.FromHours(24));
        return new AwsSnsSignatureVerifier(httpClientFactory.Object, cache, NullLogger<AwsSnsSignatureVerifier>.Instance);
    }

    private string Sign(string canonicalString, HashAlgorithmName hashAlgorithm)
    {
        var bytes = privateKey.SignData(
            Encoding.UTF8.GetBytes(canonicalString), hashAlgorithm, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(bytes);
    }

    private static string NotificationCanonicalString(SnsEnvelope envelope)
    {
        var builder = new StringBuilder();
        builder.Append("Message\n").Append(envelope.Message).Append('\n');
        builder.Append("MessageId\n").Append(envelope.MessageId).Append('\n');
        if (envelope.Subject is not null)
        {
            builder.Append("Subject\n").Append(envelope.Subject).Append('\n');
        }
        builder.Append("Timestamp\n").Append(envelope.Timestamp).Append('\n');
        builder.Append("TopicArn\n").Append(envelope.TopicArn).Append('\n');
        builder.Append("Type\n").Append(envelope.Type).Append('\n');
        return builder.ToString();
    }

    private static string SubscriptionCanonicalString(SnsEnvelope envelope)
    {
        var builder = new StringBuilder();
        builder.Append("Message\n").Append(envelope.Message).Append('\n');
        builder.Append("MessageId\n").Append(envelope.MessageId).Append('\n');
        builder.Append("SubscribeURL\n").Append(envelope.SubscribeURL).Append('\n');
        builder.Append("Timestamp\n").Append(envelope.Timestamp).Append('\n');
        builder.Append("Token\n").Append(envelope.Token).Append('\n');
        builder.Append("TopicArn\n").Append(envelope.TopicArn).Append('\n');
        builder.Append("Type\n").Append(envelope.Type).Append('\n');
        return builder.ToString();
    }

    private static SnsEnvelope NotificationEnvelope(string signature, string signatureVersion) => new(
        "Notification", "msg-1", "arn:aws:sns:us-east-1:123:topic", "{\"foo\":\"bar\"}", "2026-07-19T00:00:00Z",
        signature, signatureVersion, CertUrl, Subject: null, SubscribeURL: null, Token: null);

    private static SnsEnvelope SubscriptionEnvelope(string signature, string signatureVersion) => new(
        "SubscriptionConfirmation", "msg-2", "arn:aws:sns:us-east-1:123:topic", "confirm me",
        "2026-07-19T00:00:00Z", signature, signatureVersion, CertUrl,
        Subject: null, SubscribeURL: "https://sns.us-east-1.amazonaws.com/?Action=ConfirmSubscription", Token: "tok-1");

    [Theory]
    [InlineData("1", "SHA1")]
    [InlineData("2", "SHA256")]
    public async Task VerifyAsync_NotificationWithValidSignature_ReturnsTrue(string signatureVersion, string hashAlgorithmName)
    {
        var hashAlgorithm = new HashAlgorithmName(hashAlgorithmName);
        var unsigned = NotificationEnvelope(signature: string.Empty, signatureVersion);
        var signature = Sign(NotificationCanonicalString(unsigned), hashAlgorithm);
        var envelope = unsigned with { Signature = signature };

        var result = await CreateVerifier().VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("1", "SHA1")]
    [InlineData("2", "SHA256")]
    public async Task VerifyAsync_SubscriptionConfirmationWithValidSignature_ReturnsTrue(
        string signatureVersion, string hashAlgorithmName)
    {
        var hashAlgorithm = new HashAlgorithmName(hashAlgorithmName);
        var unsigned = SubscriptionEnvelope(signature: string.Empty, signatureVersion);
        var signature = Sign(SubscriptionCanonicalString(unsigned), hashAlgorithm);
        var envelope = unsigned with { Signature = signature };

        var result = await CreateVerifier().VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_TamperedMessage_ReturnsFalse()
    {
        var unsigned = NotificationEnvelope(signature: string.Empty, "2");
        var signature = Sign(NotificationCanonicalString(unsigned), HashAlgorithmName.SHA256);
        var envelope = (unsigned with { Signature = signature }) with { Message = "{\"foo\":\"tampered\"}" };

        var result = await CreateVerifier().VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ForgedSigningCertUrlHost_ReturnsFalseWithoutFetching()
    {
        var unsigned = NotificationEnvelope(signature: string.Empty, "2");
        var signature = Sign(NotificationCanonicalString(unsigned), HashAlgorithmName.SHA256);
        var envelope = unsigned with
        {
            Signature = signature,
            SigningCertURL = "https://evil.example.com/fake-cert.pem",
        };

        // Strict mock: any attempt to fetch a client (and thus the forged host) fails the test.
        var verifier = new AwsSnsSignatureVerifier(
            httpClientFactory.Object, cache, NullLogger<AwsSnsSignatureVerifier>.Instance);

        var result = await verifier.VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        httpClientFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_NonHttpsSigningCertUrl_ReturnsFalse()
    {
        var unsigned = NotificationEnvelope(signature: string.Empty, "2");
        var signature = Sign(NotificationCanonicalString(unsigned), HashAlgorithmName.SHA256);
        var envelope = unsigned with
        {
            Signature = signature,
            SigningCertURL = "http://sns.us-east-1.amazonaws.com/cert.pem",
        };

        var result = await CreateVerifier().VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_UnsupportedSignatureVersion_ReturnsFalse()
    {
        var envelope = NotificationEnvelope(signature: "irrelevant", signatureVersion: "3");

        var result = await CreateVerifier().VerifyAsync(envelope, TestContext.Current.CancellationToken);

        result.Should().BeFalse();
    }
}
