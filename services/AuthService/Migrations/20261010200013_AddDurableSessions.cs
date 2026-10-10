using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SessionVersion",
                table: "Users",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.Sql("UPDATE Users SET SessionVersion = UUID()");

            migrationBuilder.CreateTable(
                name: "RevokedSessions",
                columns: table => new
                {
                    TokenId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedSessions", x => x.TokenId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedSessions_ExpiresAtUtc",
                table: "RevokedSessions",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevokedSessions");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");
        }
    }
}
