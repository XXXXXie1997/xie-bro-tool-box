using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XieBox.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateTable(
                name: "EmailChallenges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Purpose = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    UserId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: true),
                    ProtectedCode = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Consumed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailChallenges", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_bin");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailChallenges_CreatedAt",
                table: "EmailChallenges",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailChallenges_Email_CreatedAt",
                table: "EmailChallenges",
                columns: new[] { "Email", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailChallenges");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
