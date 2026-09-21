using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalRecordService.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FollowUpDate",
                table: "Consultations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpInstructions",
                table: "Consultations",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpDate",
                table: "Consultations");

            migrationBuilder.DropColumn(
                name: "FollowUpInstructions",
                table: "Consultations");
        }
    }
}
