using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConnectorVersionId",
                table: "Connections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConnectorVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfigSchemaJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    IsDeprecated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectorVersions_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_ConnectorVersionId",
                table: "Connections",
                column: "ConnectorVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectorVersions_ConnectorId_Version",
                table: "ConnectorVersions",
                columns: new[] { "ConnectorId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_ConnectorVersions_ConnectorVersionId",
                table: "Connections",
                column: "ConnectorVersionId",
                principalTable: "ConnectorVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_ConnectorVersions_ConnectorVersionId",
                table: "Connections");

            migrationBuilder.DropTable(
                name: "ConnectorVersions");

            migrationBuilder.DropIndex(
                name: "IX_Connections_ConnectorVersionId",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ConnectorVersionId",
                table: "Connections");
        }
    }
}
