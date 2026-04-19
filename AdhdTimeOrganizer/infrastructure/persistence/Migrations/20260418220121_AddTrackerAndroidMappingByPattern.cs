using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackerAndroidMappingByPattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_from_template",
                schema: "public",
                table: "planner_task");

            migrationBuilder.CreateTable(
                name: "tracker_android_mapping_by_pattern",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    package_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    package_name_match_type = table.Column<int>(type: "integer", nullable: true),
                    app_label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    app_label_match_type = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_tracker_android_mapping_by_pattern", x => x.id);
                    table.CheckConstraint("CK_TrackerAndroidMappingByPattern_AppLabelMatchType", "app_label IS NULL OR app_label_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerAndroidMappingByPattern_IsIgnoredOnlyTrue", "\"is_ignored\" IS NULL OR \"is_ignored\" = TRUE");
                    table.CheckConstraint("CK_TrackerAndroidMappingByPattern_PackageNameMatchType", "package_name IS NULL OR package_name_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerAndroidMappingByPattern_TargetRequired", "(\n  CASE WHEN \"is_ignored\" = TRUE THEN 1 ELSE 0 END +\n  CASE WHEN \"activity_id\" IS NOT NULL THEN 1 ELSE 0 END +\n  CASE WHEN \"role_id\" IS NOT NULL OR \"category_id\" IS NOT NULL THEN 1 ELSE 0 END\n) = 1");
                    table.ForeignKey(
                        name: "fk_tracker_android_mapping_by_pattern_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_android_mapping_by_pattern_activity_categories_cate",
                        column: x => x.category_id,
                        principalSchema: "public",
                        principalTable: "activity_category",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_android_mapping_by_pattern_activity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "activity_role",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_android_mapping_by_pattern_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_category_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_role_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_user_id_package_name_app",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                columns: new[] { "user_id", "package_name", "app_label" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracker_android_mapping_by_pattern",
                schema: "public");

            migrationBuilder.AddColumn<bool>(
                name: "is_from_template",
                schema: "public",
                table: "planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
