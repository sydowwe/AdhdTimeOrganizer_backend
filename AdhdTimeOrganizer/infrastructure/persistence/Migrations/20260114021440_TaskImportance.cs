using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskImportance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_task_urgencies_priority_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropForeignKey(
                name: "fk_template_planner_task_task_urgencies_priority_id",
                schema: "public",
                table: "template_planner_task");

            migrationBuilder.DropColumn(
                name: "completed_tasks",
                schema: "public",
                table: "calendar");

            migrationBuilder.DropColumn(
                name: "is_planned",
                schema: "public",
                table: "calendar");

            migrationBuilder.DropColumn(
                name: "total_tasks",
                schema: "public",
                table: "calendar");

            migrationBuilder.RenameColumn(
                name: "priority_id",
                schema: "public",
                table: "template_planner_task",
                newName: "importance_id");

            migrationBuilder.RenameIndex(
                name: "ix_template_planner_task_priority_id",
                schema: "public",
                table: "template_planner_task",
                newName: "ix_template_planner_task_importance_id");

            migrationBuilder.RenameColumn(
                name: "priority_id",
                schema: "public",
                table: "planner_task",
                newName: "importance_id");

            migrationBuilder.RenameIndex(
                name: "ix_planner_task_priority_id",
                schema: "public",
                table: "planner_task",
                newName: "ix_planner_task_importance_id");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "wake_up_time",
                schema: "public",
                table: "calendar",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "bed_time",
                schema: "public",
                table: "calendar",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "task_importance",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    importance = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_importance", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_importance_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_importance",
                schema: "public",
                table: "task_importance",
                column: "importance");

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_user_id_importance",
                schema: "public",
                table: "task_importance",
                columns: new[] { "user_id", "importance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_user_id_text",
                schema: "public",
                table: "task_importance",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_planner_task_task_importances_importance_id",
                schema: "public",
                table: "planner_task",
                column: "importance_id",
                principalSchema: "public",
                principalTable: "task_importance",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_template_planner_task_task_importance_importance_id",
                schema: "public",
                table: "template_planner_task",
                column: "importance_id",
                principalSchema: "public",
                principalTable: "task_importance",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_task_importances_importance_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropForeignKey(
                name: "fk_template_planner_task_task_importance_importance_id",
                schema: "public",
                table: "template_planner_task");

            migrationBuilder.DropTable(
                name: "task_importance",
                schema: "public");

            migrationBuilder.RenameColumn(
                name: "importance_id",
                schema: "public",
                table: "template_planner_task",
                newName: "priority_id");

            migrationBuilder.RenameIndex(
                name: "ix_template_planner_task_importance_id",
                schema: "public",
                table: "template_planner_task",
                newName: "ix_template_planner_task_priority_id");

            migrationBuilder.RenameColumn(
                name: "importance_id",
                schema: "public",
                table: "planner_task",
                newName: "priority_id");

            migrationBuilder.RenameIndex(
                name: "ix_planner_task_importance_id",
                schema: "public",
                table: "planner_task",
                newName: "ix_planner_task_priority_id");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "wake_up_time",
                schema: "public",
                table: "calendar",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "bed_time",
                schema: "public",
                table: "calendar",
                type: "time without time zone",
                nullable: true,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AddColumn<int>(
                name: "completed_tasks",
                schema: "public",
                table: "calendar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_planned",
                schema: "public",
                table: "calendar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "total_tasks",
                schema: "public",
                table: "calendar",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "fk_planner_task_task_urgencies_priority_id",
                schema: "public",
                table: "planner_task",
                column: "priority_id",
                principalSchema: "public",
                principalTable: "task_priority",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_template_planner_task_task_urgencies_priority_id",
                schema: "public",
                table: "template_planner_task",
                column: "priority_id",
                principalSchema: "public",
                principalTable: "task_priority",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
