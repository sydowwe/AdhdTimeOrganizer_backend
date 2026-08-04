using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutinePeriodNudgeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ending_soon_notified_for",
                schema: "public",
                table: "routine_time_period",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "grace_notified_for",
                schema: "public",
                table: "routine_time_period",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reminder_lead_days",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_reminder_lead_days_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"reminder_lead_days\" IS NULL OR (\"reminder_lead_days\" >= 1 AND \"reminder_lead_days\" < \"length_in_days\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_reminder_lead_days_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "ending_soon_notified_for",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "grace_notified_for",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "reminder_lead_days",
                schema: "public",
                table: "routine_time_period");
        }
    }
}
