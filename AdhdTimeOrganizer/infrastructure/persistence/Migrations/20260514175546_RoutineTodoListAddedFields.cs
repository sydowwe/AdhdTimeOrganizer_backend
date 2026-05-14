using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutineTodoListAddedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_todo_list_suggested_day_range",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.RenameColumn(
                name: "suggested_day",
                schema: "public",
                table: "routine_todo_list",
                newName: "suggested_day_of_month");

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "template_planner_task",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<int[]>(
                name: "suggested_days",
                schema: "public",
                table: "routine_todo_list",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "repeating_planner_task",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoutineTodoList_SuggestedDayOfMonth_Range",
                schema: "public",
                table: "routine_todo_list",
                sql: "\"suggested_day_of_month\" IS NULL OR (\"suggested_day_of_month\" BETWEEN 1 AND 31)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RoutineTodoList_SuggestedDayOfMonth_Range",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropColumn(
                name: "suggested_days",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.RenameColumn(
                name: "suggested_day_of_month",
                schema: "public",
                table: "routine_todo_list",
                newName: "suggested_day");

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "template_planner_task",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "repeating_planner_task",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "importance_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_todo_list_suggested_day_range",
                schema: "public",
                table: "routine_todo_list",
                sql: "\"suggested_day\" IS NULL OR (\"suggested_day\" BETWEEN 1 AND 30)");
        }
    }
}
