using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepeatingPlannerTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repeating_planner_task",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    recurrence_type = table.Column<string>(type: "text", nullable: false),
                    scheduled_days = table.Column<string>(type: "text", nullable: false),
                    scheduled_dates = table.Column<string>(type: "text", nullable: false),
                    active_from_date = table.Column<DateOnly>(type: "date", nullable: true),
                    active_to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    scheduled_for_day_types = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_background = table.Column<bool>(type: "boolean", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    importance_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repeating_planner_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_repeating_planner_task_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repeating_planner_task_task_importances_importance_id",
                        column: x => x.importance_id,
                        principalSchema: "public",
                        principalTable: "task_importance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_repeating_planner_task_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repeating_planner_task_activity_id",
                schema: "public",
                table: "repeating_planner_task",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_repeating_planner_task_importance_id",
                schema: "public",
                table: "repeating_planner_task",
                column: "importance_id");

            migrationBuilder.CreateIndex(
                name: "ix_repeating_planner_task_recurrence_type",
                schema: "public",
                table: "repeating_planner_task",
                column: "recurrence_type");

            migrationBuilder.CreateIndex(
                name: "ix_repeating_planner_task_user_id_is_active",
                schema: "public",
                table: "repeating_planner_task",
                columns: new[] { "user_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repeating_planner_task",
                schema: "public");
        }
    }
}
