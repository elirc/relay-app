using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireSignature",
                table: "Webhooks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SigningSecret",
                table: "Webhooks",
                type: "TEXT",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WebhookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId_ReceivedAtUtc",
                table: "WebhookDeliveries",
                columns: new[] { "WebhookId", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "RequireSignature",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "SigningSecret",
                table: "Webhooks");
        }
    }
}
