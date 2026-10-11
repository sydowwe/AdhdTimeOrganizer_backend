using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerSlipNudges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "missed_notified_at",
                schema: "planning",
                table: "planner_task",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "overrun_notified_at",
                schema: "planning",
                table: "planner_task",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_date",
                schema: "planning",
                table: "calendar",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_calendar_date",
                schema: "planning",
                table: "calendar");

            migrationBuilder.DropColumn(
                name: "missed_notified_at",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "overrun_notified_at",
                schema: "planning",
                table: "planner_task");
        }
    }
}
