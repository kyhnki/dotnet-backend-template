using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotNetBackendTemplate.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Status_StartsAtUtc_EndsAtUtc",
                table: "Campaigns");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "Campaigns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Campaigns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status_StartsAtUtc_EndsAtUtc",
                table: "Campaigns",
                columns: new[] { "Status", "StartsAtUtc", "EndsAtUtc" },
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Campaigns_Status_StartsAtUtc_EndsAtUtc",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Campaigns");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status_StartsAtUtc_EndsAtUtc",
                table: "Campaigns",
                columns: new[] { "Status", "StartsAtUtc", "EndsAtUtc" });
        }
    }
}
