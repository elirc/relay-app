namespace Relay.Domain.Enums;

/// <summary>How a connector authenticates against the third-party service.</summary>
public enum AuthKind
{
    None = 0,
    ApiKey = 1,
    OAuth2 = 2,
    Basic = 3,
}
