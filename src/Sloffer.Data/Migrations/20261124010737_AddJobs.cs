using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sloffer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Company = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    EmploymentTypes = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PostedAt",
                table: "Jobs",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Source_ExternalId",
                table: "Jobs",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
