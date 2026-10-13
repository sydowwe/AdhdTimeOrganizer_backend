using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReminderIntervalTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                schema: "reminders",
                table: "reminder_definition",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Every recurring personal reminder already registered gets its owner's zone, so it stops drifting an hour
            // at each DST switch without waiting for the user to re-save it. The anchor's local time in that zone is the
            // time the user picked. Its stored next_occurrence_at is left as computed; the first fire after this
            // migration re-derives the series on the wall clock. Schema-qualified: hand-written SQL gets no search_path help.
            migrationBuilder.Sql("""
                UPDATE reminders.reminder_definition AS d
                SET time_zone_id = u.timezone
                FROM planning.reminder AS r
                JOIN "user"."user" AS u ON u.id = r.user_id
                WHERE d.owner_module = 'Portal'
                  AND d.subject_type = 'Reminder'
                  AND d.subject_id = r.id::text
                  AND d.schedule_type = 'RecurringInterval';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "time_zone_id",
                schema: "reminders",
                table: "reminder_definition");
        }
    }
}
