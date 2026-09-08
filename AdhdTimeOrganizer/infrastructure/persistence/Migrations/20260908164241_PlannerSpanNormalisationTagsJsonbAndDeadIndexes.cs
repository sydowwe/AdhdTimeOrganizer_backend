using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <summary>
    /// The three database-side halves of the Planning review's fixes, whose code halves landed on 2026-09-08
    /// without them: <c>MIG-3</c>, <c>MIG-4</c> and <c>MIG-5</c> (see
    /// <c>AdhdTimeOrganizer.Planning/review/03-risks-rollout.md</c>).
    /// <list type="number">
    /// <item><b><c>MIG-3</c> — normalise the spans that predate the validator rule.</b> All six planner-task
    /// validators now reject <c>EndTime &lt;= StartTime</c>, but there is no CHECK constraint and nothing had
    /// touched the rows created before the rule. Such a row round-trips through <b>no</b> write path — create,
    /// update and patch-span all refuse it — so it can only be deleted, and meanwhile
    /// <c>TaskPlannerHelper.TasksOverlap</c> reads it as a same-day negative span (no overlap, ever) and
    /// <c>PlannerTemplateApplier.IsCarvable</c> has to screen for it. Clamped rather than deleted: the row is
    /// the user's plan, and a wrong end time is recoverable where a missing task is not.</item>
    /// <item><b><c>MIG-4</c> — <c>task_planner_day_template.tags</c> from comma-joined <c>text</c> to
    /// <c>jsonb</c>.</b> The model already says <c>jsonb</c>; a migrated database still had <c>text</c>, and
    /// the integration tests never saw it because the fixture builds its schema with <c>EnsureCreated</c>.
    /// Postgres cannot cast <c>productive,focused</c> to <c>jsonb</c>, so the <c>USING</c> clause below is
    /// what makes this a data-preserving change rather than a failed deploy. Tags that a comma had already
    /// split are not recoverable — there is no record of the original.</item>
    /// <item><b><c>MIG-5</c> — drop the three dead indexes.</b> Each is one low-cardinality column with no
    /// user prefix, so no query in the slice can use it; the model stopped declaring them on 2026-09-08 and
    /// until now the database kept paying for them on every write. Pure <c>DROP INDEX</c>, no read-path risk.</item>
    /// </list>
    /// <para>
    /// <b><c>Down</c> cannot restore the clamped spans.</b> The original <c>start_time</c> / <c>end_time</c>
    /// are overwritten in place and nothing records them, so the reverse of step 1 is deliberately absent —
    /// reverting this migration restores the schema, not the data.
    /// </para>
    /// </summary>
    public partial class PlannerSpanNormalisationTagsJsonbAndDeadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MIG-3. One clamp per table, each announcing how many rows it touched: on a healthy database
            // every count is 0, and a non-zero one is the only trace these rows ever leave.
            //
            // 23:59 as the new end, and the start pulled back to 23:58 when it was 23:59 or later, which is
            // the only case where clamping the end alone would leave the row still violating the rule. Both
            // columns are `time without time zone`, so a literal is unambiguous.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    affected int;
                BEGIN
                    UPDATE planning.planner_task
                       SET start_time = LEAST(start_time, TIME '23:58'),
                           end_time   = TIME '23:59'
                     WHERE end_time <= start_time;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    RAISE NOTICE 'MIG-3: clamped % planner_task row(s) with end_time <= start_time', affected;

                    UPDATE planning.repeating_planner_task
                       SET start_time = LEAST(start_time, TIME '23:58'),
                           end_time   = TIME '23:59'
                     WHERE end_time <= start_time;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    RAISE NOTICE 'MIG-3: clamped % repeating_planner_task row(s) with end_time <= start_time', affected;

                    UPDATE planning.template_planner_task
                       SET start_time = LEAST(start_time, TIME '23:58'),
                           end_time   = TIME '23:59'
                     WHERE end_time <= start_time;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    RAISE NOTICE 'MIG-3: clamped % template_planner_task row(s) with end_time <= start_time', affected;
                END $$;
                """);

            // MIG-4. Hand-written rather than migrationBuilder.AlterColumn: EF emits a bare
            // `ALTER COLUMN tags TYPE jsonb`, which Postgres refuses on a non-empty text column (42804 —
            // "column cannot be cast automatically"). The empty-string case is carved out because
            // string_to_array('', ',') is {""}, i.e. it would invent a single empty tag.
            migrationBuilder.Sql(
                """
                -- Postgres casts the column default too, and refuses ('' is not jsonb) before it ever looks at
                -- the USING clause. The column carries no default in this schema, which makes this a no-op --
                -- and a cheap one against a database where someone added one by hand.
                ALTER TABLE planning.task_planner_day_template
                    ALTER COLUMN tags DROP DEFAULT;

                ALTER TABLE planning.task_planner_day_template
                    ALTER COLUMN tags TYPE jsonb
                    USING to_jsonb(
                        CASE WHEN tags IS NULL OR tags = '' THEN ARRAY[]::text[]
                             ELSE string_to_array(tags, ',')
                        END);
                """);

            // MIG-5.
            migrationBuilder.DropIndex(
                name: "ix_task_importance_importance",
                schema: "planning",
                table: "task_importance");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_status",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_calendar_day_type",
                schema: "planning",
                table: "calendar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reverse of MIG-4, joining the array back with the same delimiter. MIG-3 has no reverse —
            // see this class's remarks.
            //
            // Through a second column rather than an ALTER ... USING: unpacking a jsonb array needs
            // jsonb_array_elements_text, which is set-returning, and a USING transform may not contain a
            // subquery ("cannot use subquery in transform expression"). An UPDATE may. The column ends up
            // last in the table's ordering, which nothing here depends on.
            migrationBuilder.Sql(
                """
                ALTER TABLE planning.task_planner_day_template ADD COLUMN tags_text text;

                UPDATE planning.task_planner_day_template
                   SET tags_text = COALESCE((SELECT string_agg(value, ',')
                                               FROM jsonb_array_elements_text(tags) AS value), '');

                ALTER TABLE planning.task_planner_day_template DROP COLUMN tags;
                ALTER TABLE planning.task_planner_day_template RENAME COLUMN tags_text TO tags;
                ALTER TABLE planning.task_planner_day_template ALTER COLUMN tags SET NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_importance",
                schema: "planning",
                table: "task_importance",
                column: "importance");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_status",
                schema: "planning",
                table: "planner_task",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_day_type",
                schema: "planning",
                table: "calendar",
                column: "day_type");
        }
    }
}
