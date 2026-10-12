using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MedicalRecordService.Migrations
{
    /// <inheritdoc />
    public partial class InitialMedicalRecordSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConsultationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Symptoms = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExaminationFindings = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Consultations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    PatientId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    QueueId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    DoctorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    DoctorName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoomNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Symptoms = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExaminationFindings = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Diagnosis = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TemplateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_bin")
                        .Annotation("MySql:CharSet", "ascii"),
                    TemplateName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsultationDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consultations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consultations_ConsultationTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ConsultationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "ConsultationTemplates",
                columns: new[] { "Id", "CreatedAt", "ExaminationFindings", "IsActive", "Name", "Notes", "Symptoms" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Examination findings:\n- ", true, "General Consultation", "Assessment and plan:\n- ", "Presenting symptoms:\n- " },
                    { new Guid("00000000-0000-0000-0000-000000000002"), new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Respiratory examination findings:\n- ", true, "Respiratory Consultation", "Respiratory assessment and plan:\n- ", "Respiratory symptoms:\n- " },
                    { new Guid("00000000-0000-0000-0000-000000000003"), new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Abdominal examination findings:\n- ", true, "Gastrointestinal Consultation", "Gastrointestinal assessment and plan:\n- ", "Gastrointestinal symptoms:\n- " },
                    { new Guid("00000000-0000-0000-0000-000000000004"), new DateTime(2026, 9, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Musculoskeletal examination findings:\n- ", true, "Musculoskeletal Consultation", "Musculoskeletal assessment and plan:\n- ", "Musculoskeletal symptoms:\n- " }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consultations_ConsultationDate",
                table: "Consultations",
                column: "ConsultationDate");

            migrationBuilder.CreateIndex(
                name: "IX_Consultations_DoctorId",
                table: "Consultations",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Consultations_PatientId",
                table: "Consultations",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UX_Consultations_QueueId",
                table: "Consultations",
                column: "QueueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ConsultationTemplates_Name",
                table: "ConsultationTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consultations");

            migrationBuilder.DropTable(
                name: "ConsultationTemplates");
        }
    }
}
