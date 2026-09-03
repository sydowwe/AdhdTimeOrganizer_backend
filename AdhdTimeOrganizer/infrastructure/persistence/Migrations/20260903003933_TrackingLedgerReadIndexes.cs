using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackingLedgerReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id_process_name_window_start",
                schema: "tracking",
                table: "desktop_activity_entry",
                columns: new[] { "user_id", "process_name", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_session_start_utc",
                schema: "tracking",
                table: "android_session_data",
                column: "session_start_utc");

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_session_end_utc",
                schema: "tracking",
                table: "android_session_data",
                columns: new[] { "user_id", "session_end_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_desktop_activity_entry_user_id_process_name_window_start",
                schema: "tracking",
                table: "desktop_activity_entry");

            migrationBuilder.DropIndex(
                name: "ix_android_session_data_session_start_utc",
                schema: "tracking",
                table: "android_session_data");

            migrationBuilder.DropIndex(
                name: "ix_android_session_data_user_id_session_end_utc",
                schema: "tracking",
                table: "android_session_data");
        }
    }
}
