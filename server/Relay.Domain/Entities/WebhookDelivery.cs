using Relay.Domain.Enums;

namespace Relay.Domain.Entities;

/// <summary>An audit record for one inbound webhook delivery attempt.</summary>
public class WebhookDelivery
{
    public Guid Id { get; set; }

    public Guid WebhookId { get; set; }
    public Webhook? Webhook { get; set; }

    public Guid WorkspaceId { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public bool Success { get; set; }

    /// <summary>How the delivery was classified (delivered, bad signature, replay, …).</summary>
    public WebhookDeliveryOutcome Outcome { get; set; }

    /// <summary>The run this delivery produced, if any.</summary>
    public Guid? RunId { get; set; }

    public string? Detail { get; set; }
}
