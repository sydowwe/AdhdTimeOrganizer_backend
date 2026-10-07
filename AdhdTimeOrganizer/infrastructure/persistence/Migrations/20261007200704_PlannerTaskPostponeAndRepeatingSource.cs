using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerTaskPostponeAndRepeatingSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "first_due_date",
                schema: "todo",
                table: "todo_list_item",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "postpone_count",
                schema: "todo",
                table: "todo_list_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "auto_place",
                schema: "planning",
                table: "repeating_planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "original_start_time",
                schema: "planning",
                table: "planner_task",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "postpone_count",
                schema: "planning",
                table: "planner_task",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "source_repeating_task_id",
                schema: "planning",
                table: "planner_task",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_source_repeating_task_id",
                schema: "planning",
                table: "planner_task",
                column: "source_repeating_task_id");

            migrationBuilder.AddForeignKey(
                name: "fk_planner_task_repeating_planner_tasks_source_repeating_task_",
                schema: "planning",
                table: "planner_task",
                column: "source_repeating_task_id",
                principalSchema: "planning",
                principalTable: "repeating_planner_task",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_repeating_planner_tasks_source_repeating_task_",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_source_repeating_task_id",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "first_due_date",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "postpone_count",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "auto_place",
                schema: "planning",
                table: "repeating_planner_task");

            migrationBuilder.DropColumn(
                name: "original_start_time",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "postpone_count",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "source_repeating_task_id",
                schema: "planning",
                table: "planner_task");
        }
    }
}
