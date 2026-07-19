namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record SnsEnvelope(
    string Type,
    string MessageId,
    string TopicArn,
    string Message,
    string Timestamp,
    string Signature,
    string SignatureVersion,
    string SigningCertURL,
    string? Subject,
    string? SubscribeURL,
    string? Token);
