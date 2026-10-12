using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueService.Migrations
{
    /// <inheritdoc />
    public partial class AddCallNextFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalledAt",
                table: "QueueEntries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "QueueEntries",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalledAt",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "QueueEntries");
        }
    }
}
