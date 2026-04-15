using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class StepIdGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_item_todo_list_todo_list_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_item_todo_list_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                column: "todo_list_id",
                principalSchema: "public",
                principalTable: "todo_list",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_todo_list_item_todo_list_todo_list_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.AddForeignKey(
                name: "fk_todo_list_item_todo_list_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                column: "todo_list_id",
                principalSchema: "public",
                principalTable: "todo_list",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
