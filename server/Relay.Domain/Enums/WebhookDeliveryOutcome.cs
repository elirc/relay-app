namespace Relay.Domain.Enums;

/// <summary>Classification of an inbound webhook delivery attempt.</summary>
public enum WebhookDeliveryOutcome
{
    Delivered = 0,
    MissingSignature = 1,
    InvalidSignature = 2,
    TimestampExpired = 3,
    FlowDisabled = 4,
    Duplicate = 5,
}
