using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserSettingsExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ask_before_delete",
                schema: "public",
                table: "user",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "first_day_of_week",
                schema: "public",
                table: "user",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at",
                schema: "public",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "theme",
                schema: "public",
                table: "user",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ip_address",
                schema: "public",
                table: "refresh_token",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                schema: "public",
                table: "refresh_token",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ask_before_delete",
                schema: "public",
                table: "user");

            migrationBuilder.DropColumn(
                name: "first_day_of_week",
                schema: "public",
                table: "user");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                schema: "public",
                table: "user");

            migrationBuilder.DropColumn(
                name: "theme",
                schema: "public",
                table: "user");

            migrationBuilder.DropColumn(
                name: "ip_address",
                schema: "public",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "user_agent",
                schema: "public",
                table: "refresh_token");
        }
    }
}
