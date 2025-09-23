using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class better_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "is_hidden_in_view",
                schema: "public",
                table: "routine_time_period",
                newName: "is_hidden");

            migrationBuilder.CreateIndex(
                name: "ix_task_urgency_priority",
                schema: "public",
                table: "task_urgency",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_category_id",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_role_id",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "role_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_urgency_priority",
                schema: "public",
                table: "task_urgency");

            migrationBuilder.DropIndex(
                name: "ix_activity_user_id_category_id",
                schema: "public",
                table: "activity");

            migrationBuilder.DropIndex(
                name: "ix_activity_user_id_role_id",
                schema: "public",
                table: "activity");

            migrationBuilder.RenameColumn(
                name: "is_hidden",
                schema: "public",
                table: "routine_time_period",
                newName: "is_hidden_in_view");
        }
    }
}
