namespace Relay.Domain.Enums;

/// <summary>What caused a flow run to start.</summary>
public enum RunTrigger
{
    Manual = 0,
    Webhook = 1,
    Schedule = 2,
}
