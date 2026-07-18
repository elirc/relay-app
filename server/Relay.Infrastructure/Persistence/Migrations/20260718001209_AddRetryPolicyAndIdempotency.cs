using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryPolicyAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Runs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackoffSeconds",
                table: "FlowSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "FlowSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_FlowId_IdempotencyKey",
                table: "Runs",
                columns: new[] { "FlowId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Runs_FlowId_IdempotencyKey",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "BackoffSeconds",
                table: "FlowSteps");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "FlowSteps");
        }
    }
}
