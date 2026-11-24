using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sloffer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobDedup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "Jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfId",
                table: "Jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DedupKey",
                table: "Jobs",
                column: "DedupKey");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DuplicateOfId",
                table: "Jobs",
                column: "DuplicateOfId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Jobs_DuplicateOfId",
                table: "Jobs",
                column: "DuplicateOfId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Jobs_DuplicateOfId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_DedupKey",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_DuplicateOfId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "DuplicateOfId",
                table: "Jobs");
        }
    }
}
