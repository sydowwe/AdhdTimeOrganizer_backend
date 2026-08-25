using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuietHoursUserZoneAndLateDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "channel_hint",
                schema: "reminders",
                table: "reminder_kind_preference");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "originally_due_at",
                schema: "notifications",
                table: "notification",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "originally_due_at",
                schema: "notifications",
                table: "notification");

            migrationBuilder.AddColumn<string>(
                name: "channel_hint",
                schema: "reminders",
                table: "reminder_kind_preference",
                type: "text",
                nullable: true);
        }
    }
}
