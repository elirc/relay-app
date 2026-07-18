using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Security;

namespace Relay.Infrastructure.Persistence;

/// <summary>
/// Idempotent seed of the connector catalog plus a demo workspace so the app is
/// useful the moment it boots. Uses deterministic GUIDs for stable references.
/// </summary>
public static class DatabaseSeeder
{
    // Stable ids so the client/dev can rely on them.
    public static readonly Guid HttpConnectorId = new("11111111-0000-0000-0000-000000000001");
    public static readonly Guid SlackConnectorId = new("11111111-0000-0000-0000-000000000002");
    public static readonly Guid EmailConnectorId = new("11111111-0000-0000-0000-000000000003");
    public static readonly Guid SheetsConnectorId = new("11111111-0000-0000-0000-000000000004");
    public static readonly Guid DelayConnectorId = new("11111111-0000-0000-0000-000000000005");

    // Each seeded connector ships a v1 version carrying the same schema.
    public static readonly Guid HttpConnectorV1Id = new("1a1a1a1a-0000-0000-0000-000000000001");
    public static readonly Guid SlackConnectorV1Id = new("1a1a1a1a-0000-0000-0000-000000000002");
    public static readonly Guid EmailConnectorV1Id = new("1a1a1a1a-0000-0000-0000-000000000003");
    public static readonly Guid SheetsConnectorV1Id = new("1a1a1a1a-0000-0000-0000-000000000004");
    public static readonly Guid DelayConnectorV1Id = new("1a1a1a1a-0000-0000-0000-000000000005");

    public static readonly Guid DemoWorkspaceId = new("22222222-0000-0000-0000-000000000001");
    public static readonly Guid DemoUserId = new("33333333-0000-0000-0000-000000000001");
    public static readonly Guid DemoInboundConnectionId = new("44444444-0000-0000-0000-000000000001");
    public static readonly Guid DemoSlackConnectionId = new("44444444-0000-0000-0000-000000000002");
    public static readonly Guid DemoFlowId = new("55555555-0000-0000-0000-000000000001");
    public static readonly Guid DemoWebhookId = new("66666666-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions TemplateJson = new(JsonSerializerDefaults.Web);

    public static async Task SeedAsync(RelayDbContext db, bool includeDemoData = true)
    {
        await SeedConnectorsAsync(db);
        await SeedTemplatesAsync(db);
        if (includeDemoData)
        {
            await SeedDemoWorkspaceAsync(db);
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedTemplatesAsync(RelayDbContext db)
    {
        if (await db.FlowTemplates.AnyAsync()) return;

        db.FlowTemplates.AddRange(
            new FlowTemplate
            {
                Id = new Guid("88888888-0000-0000-0000-000000000001"),
                Name = "Slack alert on webhook",
                Description = "When a webhook arrives, post a message to Slack.",
                Category = "Notifications",
                TriggerConnectorKey = "http",
                StepsJson = JsonSerializer.Serialize(new[]
                {
                    new { name = "Post to Slack", connectorKey = "slack", action = "send_message", configJson = """{"text":"New event"}""", maxAttempts = 3, backoffSeconds = 0 },
                }, TemplateJson),
                CreatedAtUtc = SeedTime,
            },
            new FlowTemplate
            {
                Id = new Guid("88888888-0000-0000-0000-000000000002"),
                Name = "Log to spreadsheet",
                Description = "Append every inbound event as a row in a spreadsheet.",
                Category = "Data",
                TriggerConnectorKey = "http",
                StepsJson = JsonSerializer.Serialize(new[]
                {
                    new { name = "Append row", connectorKey = "sheets", action = "append_row", configJson = "{}", maxAttempts = 3, backoffSeconds = 0 },
                }, TemplateJson),
                CreatedAtUtc = SeedTime,
            },
            new FlowTemplate
            {
                Id = new Guid("88888888-0000-0000-0000-000000000003"),
                Name = "Delayed email follow-up",
                Description = "Wait, then send a follow-up email.",
                Category = "Notifications",
                TriggerConnectorKey = "http",
                StepsJson = JsonSerializer.Serialize(new[]
                {
                    new { name = "Wait", connectorKey = "delay", action = "wait", configJson = """{"seconds":60}""", maxAttempts = 1, backoffSeconds = 0 },
                    new { name = "Send email", connectorKey = "email", action = "send", configJson = """{"subject":"Following up"}""", maxAttempts = 3, backoffSeconds = 5 },
                }, TemplateJson),
                CreatedAtUtc = SeedTime,
            });
    }

    private static async Task SeedConnectorsAsync(RelayDbContext db)
    {
        if (await db.Connectors.AnyAsync()) return;

        db.Connectors.AddRange(
            new Connector
            {
                Id = HttpConnectorId,
                Key = "http",
                Name = "HTTP Request",
                Description = "Send an outbound HTTP request to any URL.",
                AuthKind = AuthKind.None,
                ConfigSchemaJson = """{"type":"object","properties":{"method":{"type":"string"},"url":{"type":"string"}},"required":["url"]}""",
                CreatedAtUtc = SeedTime,
            },
            new Connector
            {
                Id = SlackConnectorId,
                Key = "slack",
                Name = "Slack",
                Description = "Post messages to a Slack channel.",
                AuthKind = AuthKind.OAuth2,
                ConfigSchemaJson = """{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}""",
                CreatedAtUtc = SeedTime,
            },
            new Connector
            {
                Id = EmailConnectorId,
                Key = "email",
                Name = "Email",
                Description = "Send a transactional email.",
                AuthKind = AuthKind.ApiKey,
                ConfigSchemaJson = """{"type":"object","properties":{"from":{"type":"string"}},"required":["from"]}""",
                CreatedAtUtc = SeedTime,
            },
            new Connector
            {
                Id = SheetsConnectorId,
                Key = "sheets",
                Name = "Spreadsheet",
                Description = "Append a row to a spreadsheet.",
                AuthKind = AuthKind.OAuth2,
                ConfigSchemaJson = """{"type":"object","properties":{"spreadsheetId":{"type":"string"}},"required":["spreadsheetId"]}""",
                CreatedAtUtc = SeedTime,
            },
            new Connector
            {
                Id = DelayConnectorId,
                Key = "delay",
                Name = "Delay",
                Description = "Pause the flow for a fixed duration.",
                AuthKind = AuthKind.None,
                ConfigSchemaJson = """{"type":"object","properties":{"seconds":{"type":"integer"}},"required":["seconds"]}""",
                CreatedAtUtc = SeedTime,
            });

        // A v1 schema version per connector (mirrors the connector's schema).
        db.ConnectorVersions.AddRange(
            SeedVersion(HttpConnectorV1Id, HttpConnectorId, """{"type":"object","properties":{"method":{"type":"string"},"url":{"type":"string"}},"required":["url"]}"""),
            SeedVersion(SlackConnectorV1Id, SlackConnectorId, """{"type":"object","properties":{"channel":{"type":"string"}},"required":["channel"]}"""),
            SeedVersion(EmailConnectorV1Id, EmailConnectorId, """{"type":"object","properties":{"from":{"type":"string"}},"required":["from"]}"""),
            SeedVersion(SheetsConnectorV1Id, SheetsConnectorId, """{"type":"object","properties":{"spreadsheetId":{"type":"string"}},"required":["spreadsheetId"]}"""),
            SeedVersion(DelayConnectorV1Id, DelayConnectorId, """{"type":"object","properties":{"seconds":{"type":"integer"}},"required":["seconds"]}"""));
    }

    private static ConnectorVersion SeedVersion(Guid id, Guid connectorId, string schema) => new()
    {
        Id = id,
        ConnectorId = connectorId,
        Version = 1,
        ConfigSchemaJson = schema,
        IsDeprecated = false,
        CreatedAtUtc = SeedTime,
    };

    private static async Task SeedDemoWorkspaceAsync(RelayDbContext db)
    {
        if (await db.Workspaces.AnyAsync()) return;

        db.Workspaces.Add(new Workspace
        {
            Id = DemoWorkspaceId,
            Name = "Acme Inc.",
            Slug = "acme",
            CreatedAtUtc = SeedTime,
        });

        db.Users.Add(new User
        {
            Id = DemoUserId,
            WorkspaceId = DemoWorkspaceId,
            Email = "owner@acme.test",
            DisplayName = "Ada Owner",
            PasswordHash = PasswordHasher.Hash("password123"),
            Role = WorkspaceRole.Admin,
            CreatedAtUtc = SeedTime,
        });

        db.Connections.AddRange(
            new Connection
            {
                Id = DemoInboundConnectionId,
                WorkspaceId = DemoWorkspaceId,
                ConnectorId = HttpConnectorId,
                ConnectorVersionId = HttpConnectorV1Id,
                Name = "Inbound webhook source",
                ConfigJson = """{"url":"https://acme.test/hooks/new-signup"}""",
                Status = ConnectionStatus.Active,
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime,
            },
            new Connection
            {
                Id = DemoSlackConnectionId,
                WorkspaceId = DemoWorkspaceId,
                ConnectorId = SlackConnectorId,
                ConnectorVersionId = SlackConnectorV1Id,
                Name = "Acme #alerts",
                ConfigJson = """{"channel":"#alerts"}""",
                Status = ConnectionStatus.Active,
                CreatedAtUtc = SeedTime,
                UpdatedAtUtc = SeedTime,
            });

        db.Flows.Add(new Flow
        {
            Id = DemoFlowId,
            WorkspaceId = DemoWorkspaceId,
            Name = "Notify Slack on new signup",
            Description = "When a signup webhook arrives, post to #alerts.",
            TriggerConnectionId = DemoInboundConnectionId,
            IsEnabled = true,
            CreatedAtUtc = SeedTime,
            UpdatedAtUtc = SeedTime,
            Steps =
            {
                new FlowStep
                {
                    Id = new Guid("77777777-0000-0000-0000-000000000001"),
                    Order = 0,
                    Name = "Post to Slack",
                    ConnectionId = DemoSlackConnectionId,
                    Action = "send_message",
                    ConfigJson = """{"text":"New signup: {{payload.email}}"}""",
                },
            },
        });

        db.Webhooks.Add(new Webhook
        {
            Id = DemoWebhookId,
            WorkspaceId = DemoWorkspaceId,
            FlowId = DemoFlowId,
            Token = "demo-signup-hook",
            IsEnabled = true,
            CreatedAtUtc = SeedTime,
        });
    }
}
