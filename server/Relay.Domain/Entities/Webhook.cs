namespace Relay.Domain.Entities;

/// <summary>
/// Inbound HTTP endpoint. A POST to its unique token triggers the associated
/// flow with the request body as the run payload.
/// </summary>
public class Webhook
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public Guid FlowId { get; set; }
    public Flow? Flow { get; set; }

    /// <summary>Unguessable path segment used in the public webhook URL.</summary>
    public required string Token { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Per-endpoint HMAC signing secret, sealed with envelope encryption. When
    /// set (and <see cref="RequireSignature"/> is true), inbound requests must
    /// carry a valid signature. Never returned to clients (shown once on rotate).
    /// </summary>
    public string? SigningSecret { get; set; }

    /// <summary>Whether inbound requests must be HMAC-signed to be accepted.</summary>
    public bool RequireSignature { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastTriggeredAtUtc { get; set; }

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}
