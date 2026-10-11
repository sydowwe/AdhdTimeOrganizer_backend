using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerSuggestionDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "planner_suggestion_dismissal",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    pattern_type = table.Column<string>(type: "text", nullable: false),
                    pattern_value = table.Column<int>(type: "integer", nullable: false),
                    dismissed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planner_suggestion_dismissal", x => x.id);
                    table.ForeignKey(
                        name: "fk_planner_suggestion_dismissal_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "activity",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_planner_suggestion_dismissal_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "user",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_planner_suggestion_dismissal_activity_id",
                schema: "planning",
                table: "planner_suggestion_dismissal",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_suggestion_dismissal_user_id",
                schema: "planning",
                table: "planner_suggestion_dismissal",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_suggestion_dismissal_user_id_activity_id_pattern_ty",
                schema: "planning",
                table: "planner_suggestion_dismissal",
                columns: new[] { "user_id", "activity_id", "pattern_type", "pattern_value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planner_suggestion_dismissal",
                schema: "planning");
        }
    }
}
