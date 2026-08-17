using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeisureSuggestionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leisure_suggestion_record",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    source = table.Column<string>(type: "text", nullable: false),
                    last_suggested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_outcome = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leisure_suggestion_record", x => x.id);
                    table.ForeignKey(
                        name: "fk_leisure_suggestion_record_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_leisure_suggestion_record_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leisure_suggestion_record_activity_id",
                schema: "public",
                table: "leisure_suggestion_record",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_leisure_suggestion_record_user_id",
                schema: "public",
                table: "leisure_suggestion_record",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leisure_suggestion_record_user_id_source_activity_id",
                schema: "public",
                table: "leisure_suggestion_record",
                columns: new[] { "user_id", "source", "activity_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leisure_suggestion_record",
                schema: "public");
        }
    }
}
