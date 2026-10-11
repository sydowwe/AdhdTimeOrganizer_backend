using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class MorningBriefing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "morning_briefing_enabled",
                schema: "user",
                table: "user",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "morning_briefing_handled_for",
                schema: "user",
                table: "user",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "morning_briefing_offset_minutes",
                schema: "user",
                table: "user",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "morning_briefing_enabled",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "morning_briefing_handled_for",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "morning_briefing_offset_minutes",
                schema: "user",
                table: "user");
        }
    }
}
