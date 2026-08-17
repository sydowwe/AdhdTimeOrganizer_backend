using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TodoListCompletionRoutineFreezesAndHistoryLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_timestamp",
                schema: "public",
                table: "todo_list_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "freeze_budget",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "freeze_budget_resets_at",
                schema: "public",
                table: "routine_time_period",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "freezes_remaining",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "frozen_at",
                schema: "public",
                table: "routine_period_completions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_frozen",
                schema: "public",
                table: "routine_period_completions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "routine_todo_list_id",
                schema: "public",
                table: "activity_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "todo_list_item_id",
                schema: "public",
                table: "activity_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "paired_leisure_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_completed_timestamp",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "completed_timestamp" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_freeze_budget_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"freeze_budget\" >= 0 AND \"freeze_budget\" <= 10");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_freezes_remaining_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"freezes_remaining\" >= 0 AND \"freezes_remaining\" <= \"freeze_budget\"");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_period_completion_frozen_at_matches_is_frozen",
                schema: "public",
                table: "routine_period_completions",
                sql: "(\"is_frozen\" AND \"frozen_at\" IS NOT NULL) OR (NOT \"is_frozen\" AND \"frozen_at\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_routine_todo_list_id",
                schema: "public",
                table: "activity_history",
                column: "routine_todo_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_todo_list_item_id",
                schema: "public",
                table: "activity_history",
                column: "todo_list_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_history_routine_todo_list_routine_todo_list_id",
                schema: "public",
                table: "activity_history",
                column: "routine_todo_list_id",
                principalSchema: "public",
                principalTable: "routine_todo_list",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_history_todo_list_item_todo_list_item_id",
                schema: "public",
                table: "activity_history",
                column: "todo_list_item_id",
                principalSchema: "public",
                principalTable: "todo_list_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_item_activity_paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "paired_leisure_activity_id",
                principalSchema: "public",
                principalTable: "activity",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_history_routine_todo_list_routine_todo_list_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_history_todo_list_item_todo_list_item_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_item_activity_paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_completed_timestamp",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_freeze_budget_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_freezes_remaining_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_period_completion_frozen_at_matches_is_frozen",
                schema: "public",
                table: "routine_period_completions");

            migrationBuilder.DropIndex(
                name: "ix_activity_history_routine_todo_list_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropIndex(
                name: "ix_activity_history_todo_list_item_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropColumn(
                name: "completed_timestamp",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "paired_leisure_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "freeze_budget",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "freeze_budget_resets_at",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "freezes_remaining",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "frozen_at",
                schema: "public",
                table: "routine_period_completions");

            migrationBuilder.DropColumn(
                name: "is_frozen",
                schema: "public",
                table: "routine_period_completions");

            migrationBuilder.DropColumn(
                name: "routine_todo_list_id",
                schema: "public",
                table: "activity_history");

            migrationBuilder.DropColumn(
                name: "todo_list_item_id",
                schema: "public",
                table: "activity_history");
        }
    }
}
