using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenSessionStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "session_started_at",
                schema: "user",
                table: "refresh_token",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing sessions get their earliest surviving row. That is exact unless the cleanup has already
            // deleted the first one (rotated more than 30 days ago), in which case it is the best left.
            migrationBuilder.Sql("""
                UPDATE "user".refresh_token AS rt
                SET session_started_at = s.started_at
                FROM (SELECT session_id, MIN(created_timestamp) AS started_at
                      FROM "user".refresh_token
                      GROUP BY session_id) AS s
                WHERE rt.session_id = s.session_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "session_started_at",
                schema: "user",
                table: "refresh_token");
        }
    }
}
