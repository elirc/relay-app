using Relay.Domain.Entities;

namespace Relay.Api.Contracts.Webhooks;

public sealed record WebhookDto(
    Guid Id,
    Guid FlowId,
    string Token,
    string Url,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastTriggeredAtUtc)
{
    public static WebhookDto From(Webhook w, string baseUrl) =>
        new(w.Id, w.FlowId, w.Token, $"{baseUrl}/api/hooks/{w.Token}", w.IsEnabled, w.CreatedAtUtc, w.LastTriggeredAtUtc);
}
