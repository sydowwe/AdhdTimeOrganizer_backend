using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerAutoReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_reminder_min_importance",
                schema: "planning",
                table: "user_planner_settings",
                type: "integer",
                nullable: false,
                defaultValue: 999); // Critical (TaskImportance.CriticalMarkerValue), the entity default

            migrationBuilder.AddColumn<string>(
                name: "auto_reminder_mode",
                schema: "planning",
                table: "user_planner_settings",
                type: "text",
                nullable: false,
                defaultValue: "ImportantOnly"); // the entity default; "" would read back as Off

            migrationBuilder.AddColumn<bool>(
                name: "is_auto",
                schema: "planning",
                table: "reminder",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_reminder_min_importance",
                schema: "planning",
                table: "user_planner_settings");

            migrationBuilder.DropColumn(
                name: "auto_reminder_mode",
                schema: "planning",
                table: "user_planner_settings");

            migrationBuilder.DropColumn(
                name: "is_auto",
                schema: "planning",
                table: "reminder");
        }
    }
}
