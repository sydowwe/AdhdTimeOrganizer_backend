using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserDefaultSleepWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "default_bed_time",
                schema: "user",
                table: "user",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "default_wake_up_time",
                schema: "user",
                table: "user",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "has_custom_sleep_window",
                schema: "planning",
                table: "calendar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every day row was created with 08:00 / 00:00 (CalendarDayFactory, before FO10), so a row holding anything
            // else had its window chosen for it — edited on the day, or brought in by a template. Mark those, or the
            // first change of default would overwrite them. A day edited back to exactly 08:00 / 00:00 cannot be told
            // from an untouched one, and is left following the default, which is the window it shows anyway.
            migrationBuilder.Sql("""
                UPDATE planning.calendar
                SET has_custom_sleep_window = TRUE
                WHERE wake_up_time <> TIME '08:00' OR bed_time <> TIME '00:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_bed_time",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "default_wake_up_time",
                schema: "user",
                table: "user");

            migrationBuilder.DropColumn(
                name: "has_custom_sleep_window",
                schema: "planning",
                table: "calendar");
        }
    }
}
