using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlannerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_planner_settings",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    reminders_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    reminder_minutes_before = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    details_panel_expanded_by_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    arrow_key_nav_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    predefined_skip_reasons = table.Column<string>(type: "jsonb", nullable: false),
                    slot_duration_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    default_apply_template_id = table.Column<long>(type: "bigint", nullable: true),
                    default_conflict_resolution = table.Column<string>(type: "text", nullable: false),
                    default_apply_preview_mode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_planner_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_planner_settings_task_planner_day_templates_default_ap",
                        column: x => x.default_apply_template_id,
                        principalSchema: "public",
                        principalTable: "task_planner_day_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_planner_settings_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_planner_settings_default_apply_template_id",
                schema: "public",
                table: "user_planner_settings",
                column: "default_apply_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_planner_settings_user_id",
                schema: "public",
                table: "user_planner_settings",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_planner_settings",
                schema: "public");
        }
    }
}
