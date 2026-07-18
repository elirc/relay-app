using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowTemplatesAndExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Flows",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FlowTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TriggerConnectorKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StepsJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flows_WorkspaceId_ExternalId",
                table: "Flows",
                columns: new[] { "WorkspaceId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Flows_WorkspaceId_ExternalId",
                table: "Flows");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Flows");
        }
    }
}
