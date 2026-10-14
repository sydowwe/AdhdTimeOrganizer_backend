using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutinePeriodBestStreakBeforeRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "best_streak_before_run",
                schema: "routines",
                table: "routine_time_period",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill only where the answer is certain. A run below its best didn't set that best, so the best
            // is still the record it's chasing. A run at its best is either the record holder or tied with an
            // earlier run, and nothing on the row says which. Those keep 0, which can cost one tied run its "new
            // best" but never announces one falsely. Hand-written SQL, so the schema is named.
            migrationBuilder.Sql(
                """
                UPDATE routines.routine_time_period
                SET best_streak_before_run = best_streak
                WHERE streak < best_streak;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_time_period_best_streak_before_run_non_negative",
                schema: "routines",
                table: "routine_time_period",
                sql: "\"best_streak_before_run\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_time_period_best_streak_before_run_non_negative",
                schema: "routines",
                table: "routine_time_period");

            migrationBuilder.DropColumn(
                name: "best_streak_before_run",
                schema: "routines",
                table: "routine_time_period");
        }
    }
}
