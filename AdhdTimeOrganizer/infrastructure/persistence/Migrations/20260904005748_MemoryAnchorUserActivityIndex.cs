using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemoryAnchorUserActivityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_memory_anchor_user_id_activity_id",
                schema: "activity_profiles",
                table: "memory_anchor",
                columns: new[] { "user_id", "activity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_memory_anchor_user_id_activity_id",
                schema: "activity_profiles",
                table: "memory_anchor");
        }
    }
}
