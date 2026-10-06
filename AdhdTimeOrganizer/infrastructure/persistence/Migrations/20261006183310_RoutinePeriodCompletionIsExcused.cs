using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutinePeriodCompletionIsExcused : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_excused",
                schema: "routines",
                table: "routine_period_completions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_period_completion_not_frozen_and_excused",
                schema: "routines",
                table: "routine_period_completions",
                sql: "NOT (\"is_frozen\" AND \"is_excused\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_period_completion_not_frozen_and_excused",
                schema: "routines",
                table: "routine_period_completions");

            migrationBuilder.DropColumn(
                name: "is_excused",
                schema: "routines",
                table: "routine_period_completions");
        }
    }
}
