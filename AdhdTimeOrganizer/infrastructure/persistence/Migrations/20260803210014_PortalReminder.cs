using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortalReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remind_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lead_offsets_minutes = table.Column<string>(type: "jsonb", nullable: false),
                    recurrence = table.Column<string>(type: "text", nullable: true),
                    planner_task_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminder_planner_task_planner_task_id",
                        column: x => x.planner_task_id,
                        principalSchema: "public",
                        principalTable: "planner_task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reminder_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminder_planner_task_id",
                schema: "public",
                table: "reminder",
                column: "planner_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_user_id",
                schema: "public",
                table: "reminder",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_user_id_remind_at",
                schema: "public",
                table: "reminder",
                columns: new[] { "user_id", "remind_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder",
                schema: "public");
        }
    }
}