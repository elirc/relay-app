using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptConnectionSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CredentialsJson",
                table: "Connections");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedSecret",
                table: "Connections",
                type: "TEXT",
                maxLength: 12000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedSecret",
                table: "Connections");

            migrationBuilder.AddColumn<string>(
                name: "CredentialsJson",
                table: "Connections",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");
        }
    }
}
