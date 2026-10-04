using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerTaskAutoCompleteAtEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_complete_at_end",
                schema: "planning",
                table: "planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_auto_complete_in_progress",
                schema: "planning",
                table: "planner_task",
                column: "auto_complete_at_end",
                filter: "auto_complete_at_end AND status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_planner_task_auto_complete_in_progress",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "auto_complete_at_end",
                schema: "planning",
                table: "planner_task");
        }
    }
}
