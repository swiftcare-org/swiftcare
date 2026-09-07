using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueueService.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueDisplayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "QueueEntries",
                type: "datetime(6)",
                nullable: true);

            // Existing rows predate the dedicated event timestamp. CreatedAt is the closest
            // available historical value and avoids exposing DateTime.MinValue in the queue.
            migrationBuilder.Sql(
                "UPDATE `QueueEntries` SET `CheckedInAt` = `CreatedAt` WHERE `CheckedInAt` IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CheckedInAt",
                table: "QueueEntries",
                type: "datetime(6)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoctorName",
                table: "QueueEntries",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DoctorName",
                table: "QueueEntries");
        }
    }
}
