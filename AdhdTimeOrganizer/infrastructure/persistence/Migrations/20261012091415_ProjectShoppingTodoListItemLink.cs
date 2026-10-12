using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectShoppingTodoListItemLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_project_profile_shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile",
                column: "shopping_todo_list_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_project_profile_todo_list_item_shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile",
                column: "shopping_todo_list_item_id",
                principalSchema: "todo",
                principalTable: "todo_list_item",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_project_profile_todo_list_item_shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile");

            migrationBuilder.DropIndex(
                name: "ix_activity_project_profile_shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile");

            migrationBuilder.DropColumn(
                name: "shopping_todo_list_item_id",
                schema: "activity_profiles",
                table: "activity_project_profile");
        }
    }
}
