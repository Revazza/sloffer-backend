using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sloffer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobViewedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ViewedAt",
                table: "Jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ViewedAt",
                table: "Jobs");
        }
    }
}
