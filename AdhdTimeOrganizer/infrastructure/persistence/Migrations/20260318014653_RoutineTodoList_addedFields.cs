using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutineTodoList_addedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "task_priority",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AddColumn<int>(
                name: "best_streak",
                schema: "public",
                table: "routine_todo_list",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_completed_at",
                schema: "public",
                table: "routine_todo_list",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "streak",
                schema: "public",
                table: "routine_todo_list",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "routine_time_period",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AddColumn<int>(
                name: "best_streak",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_reset_at",
                schema: "public",
                table: "routine_time_period",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reset_anchor_day",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "streak",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "streak_grace_days",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "streak_grace_until",
                schema: "public",
                table: "routine_time_period",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "streak_threshold",
                schema: "public",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "product_name",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "activity_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoutineTodoList_BestStreak_NonNegative",
                schema: "public",
                table: "routine_todo_list",
                sql: "\"best_streak\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoutineTodoList_Streak_NonNegative",
                schema: "public",
                table: "routine_todo_list",
                sql: "\"streak\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_best_streak_non_negative",
                schema: "public",
                table: "routine_time_period",
                sql: "\"best_streak\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_length_in_days_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"length_in_days\" >= 1 AND \"length_in_days\" <= 365");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period",
                sql: "(\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0 AND \"reset_anchor_day\" BETWEEN 1 AND 7) OR (\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0 AND \"reset_anchor_day\" BETWEEN 1 AND 30)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_streak_grace_days_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"streak_grace_days\" >= 0 AND \"streak_grace_days\" <= \"length_in_days\" - 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_streak_non_negative",
                schema: "public",
                table: "routine_time_period",
                sql: "\"streak\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_streak_threshold_range",
                schema: "public",
                table: "routine_time_period",
                sql: "\"streak_threshold\" >= 1 AND \"streak_threshold\" <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RoutineTodoList_BestStreak_NonNegative",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RoutineTodoList_Streak_NonNegative",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_best_streak_non_negative",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_length_in_days_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_streak_grace_days_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_streak_non_negative",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_streak_threshold_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "best_streak",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropColumn(
                name: "last_completed_at",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropColumn(
                name: "streak",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropColumn(
                name: "best_streak",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "last_reset_at",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "reset_anchor_day",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "streak",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "streak_grace_days",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "streak_grace_until",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "streak_threshold",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "task_priority",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                schema: "public",
                table: "routine_time_period",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "product_name",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "activity_id",
                unique: true);
        }
    }
}
