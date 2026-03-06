using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityTrackingDesktop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_extension_data",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "activity_tracking_settings_desktop_entry_formatting",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    is_saved_to_main_history = table.Column<bool>(type: "boolean", nullable: false),
                    process_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    process_nice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_split = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_tracking_settings_desktop_entry_formatting", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_tracking_settings_desktop_entry_formatting_user_us",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_tracking_settings_desktop_ignored_process",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    process_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_contains_toggle = table.Column<bool>(type: "boolean", nullable: false),
                    title_contains = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_tracking_settings_desktop_ignored_process", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_tracking_settings_desktop_ignored_process_user_use",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "desktop_activity_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    record_date = table.Column<DateOnly>(type: "date", nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    window_title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    executable_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_fullscreen = table.Column<bool>(type: "boolean", nullable: false),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    background_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_playing_sound = table.Column<bool>(type: "boolean", nullable: false),
                    active_monitor = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desktop_activity_entry", x => new { x.id, x.record_date });
                    table.ForeignKey(
                        name: "fk_desktop_activity_entry_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_extension_activity_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    record_date = table.Column<DateOnly>(type: "date", nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    background_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_final = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_extension_activity_entry", x => new { x.id, x.record_date });
                    table.ForeignKey(
                        name: "fk_web_extension_activity_entry_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_tracking_settings_desktop_entry_formatting_user_id",
                schema: "public",
                table: "activity_tracking_settings_desktop_entry_formatting",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_tracking_settings_desktop_ignored_process_user_id",
                schema: "public",
                table: "activity_tracking_settings_desktop_ignored_process",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id_window_start",
                schema: "public",
                table: "desktop_activity_entry",
                columns: new[] { "user_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id_window_start_record_date_pro",
                schema: "public",
                table: "desktop_activity_entry",
                columns: new[] { "user_id", "window_start", "record_date", "process_name", "window_title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_activity_entry_user_id_window_start",
                schema: "public",
                table: "web_extension_activity_entry",
                columns: new[] { "user_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_activity_entry_user_id_window_start_domain_re",
                schema: "public",
                table: "web_extension_activity_entry",
                columns: new[] { "user_id", "window_start", "domain", "record_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_tracking_settings_desktop_entry_formatting",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_tracking_settings_desktop_ignored_process",
                schema: "public");

            migrationBuilder.DropTable(
                name: "desktop_activity_entry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "web_extension_activity_entry",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "web_extension_data",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    record_date = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    background_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_final = table.Column<bool>(type: "boolean", nullable: false),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_extension_data", x => new { x.id, x.record_date });
                    table.ForeignKey(
                        name: "fk_web_extension_data_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_data_user_id_window_start",
                schema: "public",
                table: "web_extension_data",
                columns: new[] { "user_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_data_user_id_window_start_domain_record_date",
                schema: "public",
                table: "web_extension_data",
                columns: new[] { "user_id", "window_start", "domain", "record_date" },
                unique: true);
        }
    }
}
