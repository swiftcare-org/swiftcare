using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotificationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PatientId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IsNewPatient = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    QueueId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DoctorId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    QueueNumber = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DoctorName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoomNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsultationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId",
                table: "Notifications",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OccurredAt",
                table: "Notifications",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");
        }
    }
}
