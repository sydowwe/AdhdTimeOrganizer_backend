using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <summary>
    /// Ask B9: an un-tick inside the same cycle now walks the item streak back. The column is the scored-this-cycle
    /// mark that makes that possible, holding the pre-score <c>best_streak</c> so a record the tick set can be
    /// undone with it.
    /// </summary>
    public partial class RoutineItemStreakUndoMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "best_streak_before_score",
                schema: "routines",
                table: "routine_todo_list",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                -- Rows that have already scored the open cycle must start marked, or their next completing toggle
                -- counts a second time. "Already scored" is the predicate the code derived until now: a completion
                -- on or after the day the cycle opened (never reset = any completion at all). Whether that score
                -- set a record is unknowable here, so the mark carries the current best: an un-tick on one of these
                -- rows returns the point and leaves the record alone, which errs on the side of the user.
                -- "Never completed" is default(DateTime), which Npgsql stores as -infinity (or as the literal
                -- 0001-01-01 with infinity conversions off) -- the strict > excludes both spellings.
                UPDATE routines.routine_todo_list
                   SET best_streak_before_score = best_streak
                 WHERE last_completed_at > TIMESTAMPTZ '0001-01-01 00:00:00+00'
                   AND (last_reset_date IS NULL
                        OR (last_completed_at AT TIME ZONE 'UTC')::date >= last_reset_date);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "best_streak_before_score",
                schema: "routines",
                table: "routine_todo_list");
        }
    }
}
