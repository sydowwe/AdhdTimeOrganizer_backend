using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserLastActiveAndWelcomeBack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_active_at",
                schema: "user",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "welcome_back_away_from",
                schema: "user",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "welcome_back_returned_at",
                schema: "user",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_active_at",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "welcome_back_away_from",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "welcome_back_returned_at",
                schema: "user",
                table: "user");
        }
    }
}
