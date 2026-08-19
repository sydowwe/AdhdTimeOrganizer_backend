using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                schema: "public",
                table: "activity",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_is_archived",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "is_archived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_activity_user_id_is_archived",
                schema: "public",
                table: "activity");

            migrationBuilder.DropColumn(
                name: "is_archived",
                schema: "public",
                table: "activity");
        }
    }
}
