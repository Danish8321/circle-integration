namespace TreasuryServiceOrchestrator.Application.Ports;

public sealed record SnsEnvelope(
    string Type,
    string MessageId,
    string TopicArn,
    string Message,
    string Signature,
    string SigningCertURL);
