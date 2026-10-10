using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PrescriptionService.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePrescriptionOutcomeConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Prescriptions",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "ConsultationOutcomes",
                columns: table => new
                {
                    ConsultationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationOutcomes", x => x.ConsultationId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Existing rows claim their consultation too. Conflicting historical outcomes
            // must be reconciled before applying this migration.
            migrationBuilder.Sql("""
                INSERT INTO ConsultationOutcomes (ConsultationId, Outcome)
                SELECT ConsultationId, 'PRESCRIPTION' FROM Prescriptions
                UNION ALL
                SELECT ConsultationId, 'NOT_REQUIRED' FROM NoPrescriptionDecisions;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultationOutcomes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Prescriptions");
        }
    }
}
