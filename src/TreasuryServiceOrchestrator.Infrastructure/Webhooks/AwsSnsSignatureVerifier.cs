using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace TreasuryServiceOrchestrator.Infrastructure.Webhooks;

// Standard AWS SNS message-signing verification (docs/features/03-webhook-processing.md §3.4) —
// not Circle-specific. Rejects anything whose SigningCertURL host doesn't match the AWS SNS
// certificate domain pattern before ever fetching it: trusting an attacker-chosen host would let
// a forged SigningCertURL "verify" an attacker-signed forged payload.
public sealed partial class AwsSnsSignatureVerifier(
    IHttpClientFactory httpClientFactory, IMemoryCache certCache, ILogger<AwsSnsSignatureVerifier> logger)
    : ISnsSignatureVerifier
{
    private const string HttpClientName = "AwsSnsSigningCert";

    [GeneratedRegex(@"^sns\.[a-z0-9-]+\.amazonaws\.com$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SigningCertHostPattern();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rejected SNS message {MessageId}: SigningCertURL host {Host} does not match the AWS SNS cert domain pattern.")]
    private partial void LogRejectedCertHost(string messageId, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected SNS message {MessageId}: signature verification failed.")]
    private partial void LogRejectedVerificationFailure(Exception exception, string messageId);

    public async Task<bool> VerifyAsync(SnsEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Uri.TryCreate(envelope.SigningCertURL, UriKind.Absolute, out var certUri)
                || !string.Equals(certUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || !SigningCertHostPattern().IsMatch(certUri.Host))
            {
                LogRejectedCertHost(envelope.MessageId, certUri?.Host ?? envelope.SigningCertURL);
                return false;
            }

            var canonicalString = BuildCanonicalString(envelope);
            var signatureBytes = Convert.FromBase64String(envelope.Signature);
            var hashAlgorithm = envelope.SignatureVersion switch
            {
                "1" => HashAlgorithmName.SHA1,
                "2" => HashAlgorithmName.SHA256,
                _ => throw new NotSupportedException($"Unsupported SignatureVersion '{envelope.SignatureVersion}'."),
            };

            using var certificate = await GetCertificateAsync(certUri, cancellationToken);
            using var rsa = certificate.GetRSAPublicKey()
                ?? throw new InvalidOperationException("SNS signing certificate has no RSA public key.");

            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(canonicalString), signatureBytes, hashAlgorithm, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException
            or CryptographicException or HttpRequestException)
        {
            LogRejectedVerificationFailure(ex, envelope.MessageId);
            return false;
        }
    }

    // Field set/order is fixed by AWS and differs by message Type (§3.4 item 1).
    private static string BuildCanonicalString(SnsEnvelope envelope)
    {
        var builder = new StringBuilder();

        if (string.Equals(envelope.Type, "Notification", StringComparison.Ordinal))
        {
            AppendField(builder, "Message", envelope.Message);
            AppendField(builder, "MessageId", envelope.MessageId);
            if (envelope.Subject is not null)
            {
                AppendField(builder, "Subject", envelope.Subject);
            }
            AppendField(builder, "Timestamp", envelope.Timestamp);
            AppendField(builder, "TopicArn", envelope.TopicArn);
            AppendField(builder, "Type", envelope.Type);
        }
        else
        {
            AppendField(builder, "Message", envelope.Message);
            AppendField(builder, "MessageId", envelope.MessageId);
            AppendField(builder, "SubscribeURL", envelope.SubscribeURL ?? string.Empty);
            AppendField(builder, "Timestamp", envelope.Timestamp);
            AppendField(builder, "Token", envelope.Token ?? string.Empty);
            AppendField(builder, "TopicArn", envelope.TopicArn);
            AppendField(builder, "Type", envelope.Type);
        }

        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('\n').Append(value).Append('\n');

    // Cached by SigningCertURL — the cert is static for a given URL, so this is a pure
    // latency/reliability win (§3.4 item 4), not a correctness requirement.
    private async Task<X509Certificate2> GetCertificateAsync(Uri certUri, CancellationToken cancellationToken)
    {
        if (certCache.TryGetValue<X509Certificate2>(certUri, out var cached) && cached is not null)
        {
            return cached;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var pem = await client.GetStringAsync(certUri, cancellationToken);
        var certificate = X509Certificate2.CreateFromPem(pem);

        certCache.Set(certUri, certificate, TimeSpan.FromHours(24));
        return certificate;
    }
}
