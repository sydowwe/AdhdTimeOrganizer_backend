using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class StopwatchCheckInAlarms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "phase",
                schema: "history",
                table: "timer_boundary_alarm",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "elapsed_minutes",
                schema: "history",
                table: "timer_boundary_alarm",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "history",
                table: "timer_boundary_alarm",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A pending check-in has no phase; without this it would come back as phase 0 and ring "timer finished".
            // The rows live minutes, so dropping them costs at most one check-in per running stopwatch.
            migrationBuilder.Sql("DELETE FROM history.timer_boundary_alarm WHERE kind = 1;");

            migrationBuilder.DropColumn(
                name: "elapsed_minutes",
                schema: "history",
                table: "timer_boundary_alarm");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "history",
                table: "timer_boundary_alarm");

            migrationBuilder.AlterColumn<int>(
                name: "phase",
                schema: "history",
                table: "timer_boundary_alarm",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
