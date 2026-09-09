using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TodoListsIndexRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_task_priority_id",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_task_priority_priority",
                schema: "todo",
                table: "task_priority");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_is_done_due_date",
                schema: "todo",
                table: "todo_list_item",
                columns: new[] { "user_id", "is_done", "due_date" },
                filter: "NOT is_done");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_task_priority_id_display_order",
                schema: "todo",
                table: "todo_list_item",
                columns: new[] { "user_id", "task_priority_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_todo_list_id_display_order",
                schema: "todo",
                table: "todo_list_item",
                columns: new[] { "user_id", "todo_list_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_category_id",
                schema: "todo",
                table: "todo_list",
                columns: new[] { "user_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_priority_user_id",
                schema: "todo",
                table: "task_priority",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_is_done_due_date",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_task_priority_id_display_order",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_todo_list_id_display_order",
                schema: "todo",
                table: "todo_list_item");

            migrationBuilder.DropIndex(
                name: "ix_todo_list_user_id_category_id",
                schema: "todo",
                table: "todo_list");

            migrationBuilder.DropIndex(
                name: "ix_task_priority_user_id",
                schema: "todo",
                table: "task_priority");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_task_priority_id",
                schema: "todo",
                table: "todo_list_item",
                columns: new[] { "user_id", "task_priority_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_priority_priority",
                schema: "todo",
                table: "task_priority",
                column: "priority");
        }
    }
}
