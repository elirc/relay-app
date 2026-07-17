namespace Relay.Domain.Entities;

/// <summary>Top-level tenant. Owns users, connections, flows, and webhooks.</summary>
public class Workspace
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>URL-safe unique handle, e.g. "acme".</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Connection> Connections { get; set; } = new List<Connection>();
    public ICollection<Flow> Flows { get; set; } = new List<Flow>();
}
