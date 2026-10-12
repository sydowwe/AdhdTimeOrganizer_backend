using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackerSourceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tracker_source_status",
                schema: "tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    silence_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracker_source_status", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracker_source_status_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "user",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tracker_source_status_user_id",
                schema: "tracking",
                table: "tracker_source_status",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_source_status_user_id_source",
                schema: "tracking",
                table: "tracker_source_status",
                columns: new[] { "user_id", "source" },
                unique: true);

            // P2-04 backfill: each tracker's last report, read once off the three ledgers (the row's insert time is
            // when the ingest landed), so "last seen" is right from day one. A source already silent for 48 h is
            // marked as notified: a silence that began before the feature existed is not announced, and the
            // tracker's next report clears the mark like any other. Sources are stored by enum name (EnumColumn).
            migrationBuilder.Sql("""
                INSERT INTO tracking.tracker_source_status (user_id, source, last_seen_at, silence_notified_at)
                SELECT seen.user_id,
                       seen.source,
                       seen.last_seen_at,
                       CASE WHEN seen.last_seen_at < now() - interval '48 hours' THEN now() END
                FROM (
                    SELECT user_id, 'Desktop' AS source, max(created_timestamp) AS last_seen_at
                    FROM tracking.desktop_activity_entry GROUP BY user_id
                    UNION ALL
                    SELECT user_id, 'WebExtension', max(created_timestamp)
                    FROM tracking.web_extension_activity_entry GROUP BY user_id
                    UNION ALL
                    SELECT user_id, 'Android', max(created_timestamp)
                    FROM tracking.android_session_data GROUP BY user_id
                ) AS seen;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracker_source_status",
                schema: "tracking");
        }
    }
}
