namespace Relay.Domain.Enums;

/// <summary>Execution status shared by runs and individual run steps.</summary>
public enum RunStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}
