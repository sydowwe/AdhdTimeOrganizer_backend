using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class SkipReasonKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "skip_reason_key",
                schema: "planning",
                table: "planner_task",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // B1: the six built-in labels never come back as text. A row whose stored reason equals one exactly —
            // it is already trimmed (PlannerTask.ApplyStatus) — becomes that key with the text cleared; any other
            // text, near-misses included, stays text. This is SkipReasonKeyLabels as it stood when those rows
            // were written; the table there converts new writes the same way. Values are the C# member names
            // EnumColumn stores. Schema-qualified: hand-written SQL does not get the module schema applied.
            migrationBuilder.Sql("""
                UPDATE planning.planner_task SET
                    skip_reason_key = CASE skip_reason
                        WHEN 'Ran out of time'       THEN 'NoTime'
                        WHEN 'Nestihol som'          THEN 'NoTime'
                        WHEN 'Not relevant any more' THEN 'NotRelevant'
                        WHEN 'Už nie je aktuálne'    THEN 'NotRelevant'
                        WHEN 'No energy for it'      THEN 'NoEnergy'
                        WHEN 'Nemal som energiu'     THEN 'NoEnergy'
                    END,
                    skip_reason = NULL
                WHERE skip_reason IN (
                    'Ran out of time', 'Nestihol som',
                    'Not relevant any more', 'Už nie je aktuálne',
                    'No energy for it', 'Nemal som energiu');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy: the locale each label was sent in is not recorded, so keys go back as the English label.
            migrationBuilder.Sql("""
                UPDATE planning.planner_task SET
                    skip_reason = CASE skip_reason_key
                        WHEN 'NoTime'      THEN 'Ran out of time'
                        WHEN 'NotRelevant' THEN 'Not relevant any more'
                        WHEN 'NoEnergy'    THEN 'No energy for it'
                    END
                WHERE skip_reason_key IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "skip_reason_key",
                schema: "planning",
                table: "planner_task");
        }
    }
}
