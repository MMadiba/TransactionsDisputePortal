using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewerAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Disputes_TransactionId_Live",
                table: "Disputes",
                newName: "IX_Disputes_TransactionId_NotWithdrawn");

            migrationBuilder.AddColumn<string>(
                name: "ReviewerId",
                table: "Disputes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerName",
                table: "Disputes",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_ReviewerId",
                table: "Disputes",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAtUtc_CreatedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAtUtc", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Disputes_AspNetUsers_ReviewerId",
                table: "Disputes",
                column: "ReviewerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Disputes_AspNetUsers_ReviewerId",
                table: "Disputes");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Disputes_ReviewerId",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "ReviewerId",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "ReviewerName",
                table: "Disputes");

            migrationBuilder.RenameIndex(
                name: "IX_Disputes_TransactionId_NotWithdrawn",
                table: "Disputes",
                newName: "IX_Disputes_TransactionId_Live");
        }
    }
}
