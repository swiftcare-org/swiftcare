using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalRecordService.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "Consultations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Consultations",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "IN_PROGRESS");

            migrationBuilder.CreateIndex(
                name: "UX_Consultations_EventId",
                table: "Consultations",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Consultations_EventId",
                table: "Consultations");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Consultations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Consultations");
        }
    }
}
