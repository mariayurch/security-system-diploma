using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddBootIdToSecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecurityEvents_DeviceId_EventId",
                table: "SecurityEvents");

            migrationBuilder.AddColumn<string>(
                name: "BootId",
                table: "SecurityEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_DeviceId_BootId_EventId",
                table: "SecurityEvents",
                columns: new[] { "DeviceId", "BootId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecurityEvents_DeviceId_BootId_EventId",
                table: "SecurityEvents");

            migrationBuilder.DropColumn(
                name: "BootId",
                table: "SecurityEvents");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_DeviceId_EventId",
                table: "SecurityEvents",
                columns: new[] { "DeviceId", "EventId" },
                unique: true);
        }
    }
}
