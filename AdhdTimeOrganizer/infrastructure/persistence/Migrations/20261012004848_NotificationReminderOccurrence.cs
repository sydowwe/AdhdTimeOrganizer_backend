using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationReminderOccurrence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reminder_definition_id",
                schema: "notifications",
                table: "notification",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_occurrence_at",
                schema: "notifications",
                table: "notification",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reminder_definition_id",
                schema: "notifications",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "reminder_occurrence_at",
                schema: "notifications",
                table: "notification");
        }
    }
}
