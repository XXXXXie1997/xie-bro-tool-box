using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XieBox.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingRegistrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: false),
                    ProtectedCode = table.Column<string>(type: "longtext", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingRegistrations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_bin");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_ExpiresAt",
                table: "PendingRegistrations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingRegistrations_NormalizedEmail_LastSentAt",
                table: "PendingRegistrations",
                columns: new[] { "NormalizedEmail", "LastSentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingRegistrations");
        }
    }
}
