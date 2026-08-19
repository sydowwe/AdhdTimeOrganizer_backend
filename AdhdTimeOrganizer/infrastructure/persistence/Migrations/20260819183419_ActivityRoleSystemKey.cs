using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityRoleSystemKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "system_key",
                schema: "public",
                table: "activity_role",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Backfill by the seeded English name, which is the only handle the old rows have. Those
            // names are unique per user, so this stamps at most one row per (user, key) and the unique
            // index below validates that. An account that renamed one of the three before the key
            // existed keeps a null here: there is no way to tell that row apart from a role the user
            // invented, and guessing would key the wrong role permanently. Those accounts 404 on the
            // lookup until someone maps them by hand.
            migrationBuilder.Sql(
                """
                UPDATE public.activity_role
                SET system_key = CASE name
                        WHEN 'Planner task' THEN 'PlannerTask'
                        WHEN 'To-do list task' THEN 'TodoListTask'
                        WHEN 'Routine task' THEN 'RoutineTask'
                    END
                WHERE system_key IS NULL
                  AND name IN ('Planner task', 'To-do list task', 'Routine task');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_activity_role_user_id_system_key",
                schema: "public",
                table: "activity_role",
                columns: new[] { "user_id", "system_key" },
                unique: true,
                filter: "system_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_role_user_id_system_key",
                schema: "public",
                table: "activity_role");

            migrationBuilder.DropColumn(
                name: "system_key",
                schema: "public",
                table: "activity_role");
        }
    }
}
