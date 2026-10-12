using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalRecordService.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationTemplateOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ConsultationTemplates_Name",
                table: "ConsultationTemplates");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByDoctorId",
                table: "ConsultationTemplates",
                type: "char(36)",
                nullable: true,
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.AddColumn<string>(
                name: "ActiveOwnerScope",
                table: "ConsultationTemplates",
                type: "varchar(36)",
                nullable: true,
                computedColumnSql: "(CASE WHEN `IsActive` THEN IFNULL(`CreatedByDoctorId`, '') ELSE NULL END)",
                stored: true,
                collation: "ascii_bin")
                .Annotation("MySql:CharSet", "ascii");

            migrationBuilder.CreateIndex(
                name: "UX_ConsultationTemplates_ActiveOwnerScope_Name",
                table: "ConsultationTemplates",
                columns: new[] { "ActiveOwnerScope", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ConsultationTemplates_ActiveOwnerScope_Name",
                table: "ConsultationTemplates");

            migrationBuilder.DropColumn(
                name: "ActiveOwnerScope",
                table: "ConsultationTemplates");

            migrationBuilder.DropColumn(
                name: "CreatedByDoctorId",
                table: "ConsultationTemplates");

            migrationBuilder.CreateIndex(
                name: "UX_ConsultationTemplates_Name",
                table: "ConsultationTemplates",
                column: "Name",
                unique: true);
        }
    }
}
