using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class changed_task_planner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_urgency_user_user_id",
                schema: "public",
                table: "task_urgency");

            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_task_urgency_task_urgency_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_user_id_start_timestamp",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropPrimaryKey(
                name: "pk_task_urgency",
                schema: "public",
                table: "task_urgency");

            migrationBuilder.DropColumn(
                name: "color",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "start_timestamp",
                schema: "public",
                table: "planner_task");

            migrationBuilder.RenameTable(
                name: "task_urgency",
                schema: "public",
                newName: "task_priority",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "task_urgency_id",
                schema: "public",
                table: "todo_list",
                newName: "task_priority_id");

            migrationBuilder.RenameIndex(
                name: "ix_todo_list_user_id_task_urgency_id",
                schema: "public",
                table: "todo_list",
                newName: "ix_todo_list_user_id_task_priority_id");

            migrationBuilder.RenameIndex(
                name: "ix_todo_list_task_urgency_id",
                schema: "public",
                table: "todo_list",
                newName: "ix_todo_list_task_priority_id");

            migrationBuilder.RenameColumn(
                name: "minute_length",
                schema: "public",
                table: "planner_task",
                newName: "status");

            migrationBuilder.RenameIndex(
                name: "ix_task_urgency_user_id_text",
                schema: "public",
                table: "task_priority",
                newName: "ix_task_priority_user_id_text");

            migrationBuilder.RenameIndex(
                name: "ix_task_urgency_user_id_priority",
                schema: "public",
                table: "task_priority",
                newName: "ix_task_priority_user_id_priority");

            migrationBuilder.RenameIndex(
                name: "ix_task_urgency_priority",
                schema: "public",
                table: "task_priority",
                newName: "ix_task_priority_priority");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "actual_end_time",
                schema: "public",
                table: "planner_task",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "actual_start_time",
                schema: "public",
                table: "planner_task",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "calendar_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                schema: "public",
                table: "planner_task",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "is_background",
                schema: "public",
                table: "planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_from_template",
                schema: "public",
                table: "planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_optional",
                schema: "public",
                table: "planner_task",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "public",
                table: "planner_task",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "public",
                table: "planner_task",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "priority_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "skip_reason",
                schema: "public",
                table: "planner_task",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_template_task_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                schema: "public",
                table: "planner_task",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<long>(
                name: "todolist_id",
                schema: "public",
                table: "planner_task",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_task_priority",
                schema: "public",
                table: "task_priority",
                column: "id");

            migrationBuilder.CreateTable(
                name: "calendar",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_type = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    wake_up_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    bed_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    is_planned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    applied_template_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    weather = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_tasks = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completed_tasks = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_planner_day_template",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    default_wake_up_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    default_bed_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    suggested_for_day_type = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "text", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_planner_day_template", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_planner_day_template_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_planner_task",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    template_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_background = table.Column<bool>(type: "boolean", nullable: false),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    priority_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_planner_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_planner_task_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_planner_task_task_planner_day_template_template_id",
                        column: x => x.template_id,
                        principalSchema: "public",
                        principalTable: "task_planner_day_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_planner_task_task_urgencies_priority_id",
                        column: x => x.priority_id,
                        principalSchema: "public",
                        principalTable: "task_priority",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_template_planner_task_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_calendar_id",
                schema: "public",
                table: "planner_task",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_priority_id",
                schema: "public",
                table: "planner_task",
                column: "priority_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_status",
                schema: "public",
                table: "planner_task",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_todolist_id",
                schema: "public",
                table: "planner_task",
                column: "todolist_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_user_id_calendar_id_start_time",
                schema: "public",
                table: "planner_task",
                columns: new[] { "user_id", "calendar_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_day_type",
                schema: "public",
                table: "calendar",
                column: "day_type");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_user_id_date",
                schema: "public",
                table: "calendar",
                columns: new[] { "user_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_planner_day_template_suggested_for_day_type",
                schema: "public",
                table: "task_planner_day_template",
                column: "suggested_for_day_type");

            migrationBuilder.CreateIndex(
                name: "ix_task_planner_day_template_user_id_name",
                schema: "public",
                table: "task_planner_day_template",
                columns: new[] { "user_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_activity_id",
                schema: "public",
                table: "template_planner_task",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_priority_id",
                schema: "public",
                table: "template_planner_task",
                column: "priority_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_template_id_start_time",
                schema: "public",
                table: "template_planner_task",
                columns: new[] { "template_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_user_id",
                schema: "public",
                table: "template_planner_task",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_planner_task_calendar_calendar_id",
                schema: "public",
                table: "planner_task",
                column: "calendar_id",
                principalSchema: "public",
                principalTable: "calendar",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

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
                name: "fk_planner_task_todo_lists_todolist_id",
                schema: "public",
                table: "planner_task",
                column: "todolist_id",
                principalSchema: "public",
                principalTable: "todo_list",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_task_priority_user_user_id",
                schema: "public",
                table: "task_priority",
                column: "user_id",
                principalSchema: "public",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_task_priority_task_priority_id",
                schema: "public",
                table: "todo_list",
                column: "task_priority_id",
                principalSchema: "public",
                principalTable: "task_priority",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_calendar_calendar_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_task_urgencies_priority_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropForeignKey(
                name: "fk_planner_task_todo_lists_todolist_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropForeignKey(
                name: "fk_task_priority_user_user_id",
                schema: "public",
                table: "task_priority");

            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_task_priority_task_priority_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropTable(
                name: "calendar",
                schema: "public");

            migrationBuilder.DropTable(
                name: "template_planner_task",
                schema: "public");

            migrationBuilder.DropTable(
                name: "task_planner_day_template",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_calendar_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_priority_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_status",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_todolist_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropIndex(
                name: "ix_planner_task_user_id_calendar_id_start_time",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropPrimaryKey(
                name: "pk_task_priority",
                schema: "public",
                table: "task_priority");

            migrationBuilder.DropColumn(
                name: "actual_end_time",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "actual_start_time",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "calendar_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "end_time",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "is_background",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "is_from_template",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "is_optional",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "priority_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "skip_reason",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "source_template_task_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "start_time",
                schema: "public",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "todolist_id",
                schema: "public",
                table: "planner_task");

            migrationBuilder.RenameTable(
                name: "task_priority",
                schema: "public",
                newName: "task_urgency",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "task_priority_id",
                schema: "public",
                table: "todo_list",
                newName: "task_urgency_id");

            migrationBuilder.RenameIndex(
                name: "ix_todo_list_user_id_task_priority_id",
                schema: "public",
                table: "todo_list",
                newName: "ix_todo_list_user_id_task_urgency_id");

            migrationBuilder.RenameIndex(
                name: "ix_todo_list_task_priority_id",
                schema: "public",
                table: "todo_list",
                newName: "ix_todo_list_task_urgency_id");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "public",
                table: "planner_task",
                newName: "minute_length");

            migrationBuilder.RenameIndex(
                name: "ix_task_priority_user_id_text",
                schema: "public",
                table: "task_urgency",
                newName: "ix_task_urgency_user_id_text");

            migrationBuilder.RenameIndex(
                name: "ix_task_priority_user_id_priority",
                schema: "public",
                table: "task_urgency",
                newName: "ix_task_urgency_user_id_priority");

            migrationBuilder.RenameIndex(
                name: "ix_task_priority_priority",
                schema: "public",
                table: "task_urgency",
                newName: "ix_task_urgency_priority");

            migrationBuilder.AddColumn<string>(
                name: "color",
                schema: "public",
                table: "planner_task",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "start_timestamp",
                schema: "public",
                table: "planner_task",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddPrimaryKey(
                name: "pk_task_urgency",
                schema: "public",
                table: "task_urgency",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_user_id_start_timestamp",
                schema: "public",
                table: "planner_task",
                columns: new[] { "user_id", "start_timestamp" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_task_urgency_user_user_id",
                schema: "public",
                table: "task_urgency",
                column: "user_id",
                principalSchema: "public",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_task_urgency_task_urgency_id",
                schema: "public",
                table: "todo_list",
                column: "task_urgency_id",
                principalSchema: "public",
                principalTable: "task_urgency",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
