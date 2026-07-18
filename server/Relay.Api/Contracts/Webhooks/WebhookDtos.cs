using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Api.Contracts.Webhooks;

public sealed record WebhookDto(
    Guid Id,
    Guid FlowId,
    string Token,
    string Url,
    bool IsEnabled,
    bool RequireSignature,
    bool HasSigningSecret,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastTriggeredAtUtc)
{
    public static WebhookDto From(Webhook w, string baseUrl) =>
        new(w.Id, w.FlowId, w.Token, $"{baseUrl}/api/hooks/{w.Token}", w.IsEnabled,
            w.RequireSignature, !string.IsNullOrWhiteSpace(w.SigningSecret),
            w.CreatedAtUtc, w.LastTriggeredAtUtc);
}

/// <summary>Returned once when a signing secret is generated — never stored in plaintext or echoed again.</summary>
public sealed record SigningSecretResponse(string SigningSecret, string TimestampHeader, string SignatureHeader);

public sealed record WebhookDeliveryDto(
    Guid Id,
    DateTimeOffset ReceivedAtUtc,
    bool Success,
    WebhookDeliveryOutcome Outcome,
    Guid? RunId,
    string? Detail)
{
    public static WebhookDeliveryDto From(WebhookDelivery d) =>
        new(d.Id, d.ReceivedAtUtc, d.Success, d.Outcome, d.RunId, d.Detail);
}
