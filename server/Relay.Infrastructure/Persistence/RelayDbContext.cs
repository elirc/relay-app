using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;

namespace Relay.Infrastructure.Persistence;

public class RelayDbContext : DbContext
{
    public RelayDbContext(DbContextOptions<RelayDbContext> options) : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Connector> Connectors => Set<Connector>();
    public DbSet<ConnectorVersion> ConnectorVersions => Set<ConnectorVersion>();
    public DbSet<FlowTemplate> FlowTemplates => Set<FlowTemplate>();
    public DbSet<Connection> Connections => Set<Connection>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowStep> FlowSteps => Set<FlowStep>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunStepLog> RunStepLogs => Set<RunStepLog>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite-safe DateTimeOffset storage (see converter docs).
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUtcTicksConverter>();

        // Persist enums as readable strings so re-ordering members is safe.
        configurationBuilder.Properties<AuthKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ConnectionStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RunStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RunTrigger>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<WorkspaceRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<WebhookDeliveryOutcome>().HaveConversion<string>().HaveMaxLength(30);

        configurationBuilder.Properties<string>().AreUnicode().HaveMaxLength(1024);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Workspace>(e =>
        {
            e.HasIndex(w => w.Slug).IsUnique();
            e.Property(w => w.Name).HasMaxLength(200);
            e.Property(w => w.Slug).HasMaxLength(100);
        });

        b.Entity<User>(e =>
        {
            // Email unique within a workspace.
            e.HasIndex(u => new { u.WorkspaceId, u.Email }).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasOne(u => u.Workspace)
                .WithMany(w => w.Users)
                .HasForeignKey(u => u.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Connector>(e =>
        {
            e.HasIndex(c => c.Key).IsUnique();
            e.Property(c => c.ConfigSchemaJson).HasMaxLength(8000);
            e.Property(c => c.Description).HasMaxLength(2000);
        });

        b.Entity<FlowTemplate>(e =>
        {
            e.Property(t => t.Description).HasMaxLength(2000);
            e.Property(t => t.Category).HasMaxLength(100);
            e.Property(t => t.TriggerConnectorKey).HasMaxLength(100);
            e.Property(t => t.StepsJson).HasMaxLength(8000);
        });

        b.Entity<ConnectorVersion>(e =>
        {
            e.Property(v => v.ConfigSchemaJson).HasMaxLength(8000);
            e.HasIndex(v => new { v.ConnectorId, v.Version }).IsUnique();
            e.HasOne(v => v.Connector)
                .WithMany(c => c.Versions)
                .HasForeignKey(v => v.ConnectorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Connection>(e =>
        {
            e.Property(c => c.ConfigJson).HasMaxLength(8000);
            e.Property(c => c.EncryptedSecret).HasMaxLength(12000);
            e.HasIndex(c => new { c.WorkspaceId, c.Name });
            e.HasOne(c => c.Workspace)
                .WithMany(w => w.Connections)
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Connector)
                .WithMany(con => con.Connections)
                .HasForeignKey(c => c.ConnectorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.ConnectorVersion)
                .WithMany()
                .HasForeignKey(c => c.ConnectorVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Flow>(e =>
        {
            e.Property(f => f.Description).HasMaxLength(2000);
            e.Property(f => f.ExternalId).HasMaxLength(200);
            e.Property(f => f.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(f => new { f.WorkspaceId, f.Name });
            // Unique per workspace among non-null external ids (idempotent import).
            e.HasIndex(f => new { f.WorkspaceId, f.ExternalId }).IsUnique();
            e.HasOne(f => f.Workspace)
                .WithMany(w => w.Flows)
                .HasForeignKey(f => f.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            // A trigger connection must not be deletable out from under a flow.
            e.HasOne(f => f.TriggerConnection)
                .WithMany()
                .HasForeignKey(f => f.TriggerConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<FlowStep>(e =>
        {
            e.Property(s => s.ConfigJson).HasMaxLength(8000);
            e.HasIndex(s => new { s.FlowId, s.Order }).IsUnique();
            e.HasOne(s => s.Flow)
                .WithMany(f => f.Steps)
                .HasForeignKey(s => s.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Connection)
                .WithMany()
                .HasForeignKey(s => s.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Run>(e =>
        {
            e.Property(r => r.Error).HasMaxLength(4000);
            e.Property(r => r.TriggerPayloadJson).HasMaxLength(8000);
            e.Property(r => r.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(r => new { r.FlowId, r.StartedAtUtc });
            // Unique per flow among non-null keys (SQLite treats NULLs as distinct).
            e.HasIndex(r => new { r.FlowId, r.IdempotencyKey }).IsUnique();
            e.HasOne(r => r.Flow)
                .WithMany(f => f.Runs)
                .HasForeignKey(r => r.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RunStepLog>(e =>
        {
            e.Property(l => l.Message).HasMaxLength(8000);
            e.HasIndex(l => new { l.RunId, l.StepOrder });
            e.HasOne(l => l.Run)
                .WithMany(r => r.StepLogs)
                .HasForeignKey(l => l.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.FlowStep)
                .WithMany()
                .HasForeignKey(l => l.FlowStepId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Webhook>(e =>
        {
            e.HasIndex(w => w.Token).IsUnique();
            e.Property(w => w.Token).HasMaxLength(64);
            e.Property(w => w.SigningSecret).HasMaxLength(12000);
            e.HasOne(w => w.Workspace)
                .WithMany()
                .HasForeignKey(w => w.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.Flow)
                .WithMany(f => f.Webhooks)
                .HasForeignKey(w => w.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WebhookDelivery>(e =>
        {
            e.Property(d => d.Detail).HasMaxLength(2000);
            e.HasIndex(d => new { d.WebhookId, d.ReceivedAtUtc });
            e.HasOne(d => d.Webhook)
                .WithMany(w => w.Deliveries)
                .HasForeignKey(d => d.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Schedule>(e =>
        {
            e.Property(s => s.CronExpression).HasMaxLength(120);
            e.HasIndex(s => new { s.FlowId, s.NextRunAtUtc });
            e.HasOne(s => s.Workspace)
                .WithMany()
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Flow)
                .WithMany(f => f.Schedules)
                .HasForeignKey(s => s.FlowId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
