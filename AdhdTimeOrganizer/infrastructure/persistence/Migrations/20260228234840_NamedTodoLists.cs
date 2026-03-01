using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class NamedTodoLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_activity_activity_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_task_priority_task_priority_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_activity_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_task_priority_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_user_id_activity_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_user_id_task_priority_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TodoList_DoneCount_LessOrEqual_TotalCount",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TodoList_DoneCount_Min",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TodoList_TotalCount_Range",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "activity_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "display_order",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "done_count",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "is_done",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "task_priority_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "total_count",
                schema: "public",
                table: "todo_list");

            migrationBuilder.AddColumn<long>(
                name: "category_id",
                schema: "public",
                table: "todo_list",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon",
                schema: "public",
                table: "todo_list",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                schema: "public",
                table: "todo_list",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "text",
                schema: "public",
                table: "todo_list",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "todo_list_category",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list_category", x => x.id);
                    table.ForeignKey(
                        name: "fk_todo_list_category_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todo_list_item",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    task_priority_id = table.Column<long>(type: "bigint", nullable: false),
                    todo_list_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    done_count = table.Column<int>(type: "integer", nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list_item", x => x.id);
                    table.CheckConstraint("CK_TodoListItem_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
                    table.CheckConstraint("CK_TodoListItem_DoneCount_Min", "done_count IS NULL OR done_count >= 0");
                    table.CheckConstraint("CK_TodoListItem_TotalCount_Range", "total_count IS NULL OR total_count >= 2 AND total_count <= 99");
                    table.ForeignKey(
                        name: "fk_todo_list_item_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_todo_list_item_task_priority_task_priority_id",
                        column: x => x.task_priority_id,
                        principalSchema: "public",
                        principalTable: "task_priority",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_todo_list_item_todo_list_todo_list_id",
                        column: x => x.todo_list_id,
                        principalSchema: "public",
                        principalTable: "todo_list",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_todo_list_item_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_category_id",
                schema: "public",
                table: "todo_list",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_name",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_category_user_id_name",
                schema: "public",
                table: "todo_list_category",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_task_priority_id",
                schema: "public",
                table: "todo_list_item",
                column: "task_priority_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                column: "todo_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_activity_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_task_priority_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "task_priority_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_todo_list_category_category_id",
                schema: "public",
                table: "todo_list",
                column: "category_id",
                principalSchema: "public",
                principalTable: "todo_list_category",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_todo_list_category_category_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropTable(
                name: "todo_list_category",
                schema: "public");

            migrationBuilder.DropTable(
                name: "todo_list_item",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_category_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_user_id_name",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "icon",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "name",
                schema: "public",
                table: "todo_list");

            migrationBuilder.DropColumn(
                name: "text",
                schema: "public",
                table: "todo_list");

            migrationBuilder.AddColumn<long>(
                name: "activity_id",
                schema: "public",
                table: "todo_list",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "display_order",
                schema: "public",
                table: "todo_list",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "done_count",
                schema: "public",
                table: "todo_list",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_done",
                schema: "public",
                table: "todo_list",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "task_priority_id",
                schema: "public",
                table: "todo_list",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "total_count",
                schema: "public",
                table: "todo_list",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_activity_id",
                schema: "public",
                table: "todo_list",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_task_priority_id",
                schema: "public",
                table: "todo_list",
                column: "task_priority_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_activity_id",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_task_priority_id",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "task_priority_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TodoList_DoneCount_LessOrEqual_TotalCount",
                schema: "public",
                table: "todo_list",
                sql: "done_count <= total_count");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TodoList_DoneCount_Min",
                schema: "public",
                table: "todo_list",
                sql: "done_count IS NULL OR done_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TodoList_TotalCount_Range",
                schema: "public",
                table: "todo_list",
                sql: "total_count IS NULL OR total_count >= 2 AND total_count <= 99");

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_activity_activity_id",
                schema: "public",
                table: "todo_list",
                column: "activity_id",
                principalSchema: "public",
                principalTable: "activity",
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
    }
}
