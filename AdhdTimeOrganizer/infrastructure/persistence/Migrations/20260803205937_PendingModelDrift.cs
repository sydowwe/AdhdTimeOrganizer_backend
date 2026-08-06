using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_login_at",
                schema: "public",
                table: "user");

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_activity_entry_user_id",
                schema: "public",
                table: "web_extension_activity_entry",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_user_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_user_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id",
                schema: "public",
                table: "todo_list_item",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_category_user_id",
                schema: "public",
                table: "todo_list_category",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id",
                schema: "public",
                table: "todo_list",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_planner_day_template_user_id",
                schema: "public",
                table: "task_planner_day_template",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_routine_todo_list_user_id",
                schema: "public",
                table: "routine_todo_list",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_repeating_planner_task_user_id",
                schema: "public",
                table: "repeating_planner_task",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_user_id",
                schema: "public",
                table: "planner_task",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id",
                schema: "public",
                table: "desktop_activity_entry",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_user_id",
                schema: "public",
                table: "calendar",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id",
                schema: "public",
                table: "android_session_data",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_weather_dependency_user_id",
                schema: "public",
                table: "activity_weather_dependency",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_role_user_id",
                schema: "public",
                table: "activity_role",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_location_type_user_id",
                schema: "public",
                table: "activity_location_type",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_user_id",
                schema: "public",
                table: "activity_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_experience_type_user_id",
                schema: "public",
                table: "activity_experience_type",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_expected_cost_tier_user_id",
                schema: "public",
                table: "activity_expected_cost_tier",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_category_user_id",
                schema: "public",
                table: "activity_category",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id",
                schema: "public",
                table: "activity",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_web_extension_activity_entry_user_id",
                schema: "public",
                table: "web_extension_activity_entry");

            migrationBuilder.DropIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_user_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern");

            migrationBuilder.DropIndex(
                name: "ix_tracker_android_mapping_by_pattern_user_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_category_user_id",
                schema: "public",
                table: "todo_list_category");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_user_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_task_planner_day_template_user_id",
                schema: "public",
                table: "task_planner_day_template");

            migrationBuilder.DropIndex(
                name: "ix_routine_todo_list_user_id",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropIndex(
                name: "ix_repeating_planner_task_user_id",
                schema: "public",
                table: "repeating_planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_user_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_desktop_activity_entry_user_id",
                schema: "public",
                table: "desktop_activity_entry");

            migrationBuilder.DropIndex(
                name: "ix_calendar_user_id",
                schema: "public",
                table: "calendar");

            migrationBuilder.DropIndex(
                name: "ix_android_session_data_user_id",
                schema: "public",
                table: "android_session_data");

            migrationBuilder.DropIndex(
                name: "ix_activity_weather_dependency_user_id",
                schema: "public",
                table: "activity_weather_dependency");

            migrationBuilder.DropIndex(
                name: "ix_activity_role_user_id",
                schema: "public",
                table: "activity_role");

            migrationBuilder.DropIndex(
                name: "ix_activity_location_type_user_id",
                schema: "public",
                table: "activity_location_type");

            migrationBuilder.DropIndex(
                name: "ix_activity_history_user_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropIndex(
                name: "ix_activity_experience_type_user_id",
                schema: "public",
                table: "activity_experience_type");

            migrationBuilder.DropIndex(
                name: "ix_activity_expected_cost_tier_user_id",
                schema: "public",
                table: "activity_expected_cost_tier");

            migrationBuilder.DropIndex(
                name: "ix_activity_category_user_id",
                schema: "public",
                table: "activity_category");

            migrationBuilder.DropIndex(
                name: "ix_activity_user_id",
                schema: "public",
                table: "activity");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at",
                schema: "public",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}