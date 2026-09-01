using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityCategorySetNullOnDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_activity_categories_category_id",
                schema: "activity",
                table: "activity");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_activity_categories_category_id",
                schema: "activity",
                table: "activity",
                column: "category_id",
                principalSchema: "activity",
                principalTable: "activity_category",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_activity_categories_category_id",
                schema: "activity",
                table: "activity");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_activity_categories_category_id",
                schema: "activity",
                table: "activity",
                column: "category_id",
                principalSchema: "activity",
                principalTable: "activity_category",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
