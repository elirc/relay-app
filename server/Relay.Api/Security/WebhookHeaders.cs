namespace Relay.Api.Security;

/// <summary>Header names carrying the HMAC signature material on inbound webhooks.</summary>
public static class WebhookHeaders
{
    public const string Timestamp = "X-Relay-Timestamp";
    public const string Signature = "X-Relay-Signature";
}
