using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixRoutineTimePeriodResetAnchorDayCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period",
                sql: "((\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0) AND \"reset_anchor_day\" BETWEEN 0 AND 7) OR ((\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0) AND \"reset_anchor_day\" BETWEEN 0 AND 30)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period");

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_reset_anchor_day_range",
                schema: "public",
                table: "routine_time_period",
                sql: "(\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0 AND \"reset_anchor_day\" BETWEEN 1 AND 7) OR (\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0 AND \"reset_anchor_day\" BETWEEN 1 AND 30)");
        }
    }
}
