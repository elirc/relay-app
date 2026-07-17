namespace Relay.Domain.Enums;

/// <summary>Lifecycle state of an installed connector instance.</summary>
public enum ConnectionStatus
{
    Active = 0,
    Disabled = 1,
    Error = 2,
}
