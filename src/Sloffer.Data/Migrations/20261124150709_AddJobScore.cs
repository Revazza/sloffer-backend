using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sloffer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreReasons",
                table: "Jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreVersion",
                table: "Jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScoredAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Score",
                table: "Jobs",
                column: "Score");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_Score",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ScoreReasons",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ScoreVersion",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ScoredAt",
                table: "Jobs");
        }
    }
}
