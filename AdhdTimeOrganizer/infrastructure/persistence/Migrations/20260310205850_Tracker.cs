using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "web_extension_data",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "android_session_data",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    app_label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    session_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    session_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_android_session_data", x => x.id);
                    table.ForeignKey(
                        name: "fk_android_session_data_user_user_id",
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
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    window_title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
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
                name: "tracker_desktop_mapping_by_pattern",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    process_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    process_name_match_type = table.Column<int>(type: "integer", nullable: true),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    product_name_match_type = table.Column<int>(type: "integer", nullable: true),
                    window_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    window_title_match_type = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_ignored = table.Column<bool>(type: "boolean", nullable: true),
                    activity_id = table.Column<long>(type: "bigint", nullable: true),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracker_desktop_mapping_by_pattern", x => x.id);
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_IsIgnoredOnlyTrue", "\"is_ignored\" IS NULL OR \"is_ignored\" = TRUE");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_ProcessNameMatchType", "process_name IS NULL OR process_name_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_ProductNameMatchType", "product_name IS NULL OR product_name_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_TargetRequired", "(\r\n  CASE WHEN \"is_ignored\" = TRUE THEN 1 ELSE 0 END +\r\n  CASE WHEN \"activity_id\" IS NOT NULL THEN 1 ELSE 0 END +\r\n  CASE WHEN \"role_id\" IS NOT NULL OR \"category_id\" IS NOT NULL THEN 1 ELSE 0 END\r\n) = 1");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_WindowTitleMatchType", "window_title IS NULL OR window_title_match_type IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activity_categories_cate",
                        column: x => x.category_id,
                        principalSchema: "public",
                        principalTable: "activity_category",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "activity_role",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_user_user_id",
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
                name: "ix_android_session_data_user_id_device_id_package_name_session",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "device_id", "package_name", "session_start_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_package_name",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "package_name" });

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_session_start_utc",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "session_start_utc" });

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
                name: "ix_tracker_desktop_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_category_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_role_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_user_id_process_name_pro",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                columns: new[] { "user_id", "process_name", "product_name", "window_title" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

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
                name: "android_session_data",
                schema: "public");

            migrationBuilder.DropTable(
                name: "desktop_activity_entry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tracker_desktop_mapping_by_pattern",
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
