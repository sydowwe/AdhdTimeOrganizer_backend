using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoutineTimePeriodVisibleUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_routine_time_period_user_id_visible",
                schema: "routines",
                table: "routine_time_period",
                column: "user_id",
                filter: "NOT is_hidden");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_routine_time_period_user_id_visible",
                schema: "routines",
                table: "routine_time_period");
        }
    }
}
