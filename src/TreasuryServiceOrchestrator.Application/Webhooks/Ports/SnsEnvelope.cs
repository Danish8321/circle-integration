namespace TreasuryServiceOrchestrator.Application.Webhooks.Ports;

public sealed record SnsEnvelope(
    string Type,
    string MessageId,
    string TopicArn,
    string Message,
    string Signature,
    string SigningCertURL);
