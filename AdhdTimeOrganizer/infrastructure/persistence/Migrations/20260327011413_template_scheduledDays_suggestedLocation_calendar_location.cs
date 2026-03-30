using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class template_scheduledDays_suggestedLocation_calendar_location : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scheduled_days",
                schema: "public",
                table: "task_planner_day_template",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "suggested_location",
                schema: "public",
                table: "task_planner_day_template",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "public",
                table: "calendar",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scheduled_days",
                schema: "public",
                table: "task_planner_day_template");

            migrationBuilder.DropColumn(
                name: "suggested_location",
                schema: "public",
                table: "task_planner_day_template");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "public",
                table: "calendar");
        }
    }
}
