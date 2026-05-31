using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class hotfix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_diy_profile",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "activity_project_profile",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    difficulty_level = table.Column<string>(type: "text", nullable: false),
                    project_area = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    estimated_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    is_messy = table.Column<bool>(type: "boolean", nullable: false),
                    materials_needed = table.Column<string>(type: "jsonb", nullable: false),
                    required_tools = table.Column<string>(type: "jsonb", nullable: false),
                    readiness_status = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_project_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_project_profile_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_project_profile_activity_id",
                schema: "public",
                table: "activity_project_profile",
                column: "activity_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_project_profile",
                schema: "public");

            migrationBuilder.CreateTable(
                name: "activity_diy_profile",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    difficulty_level = table.Column<string>(type: "text", nullable: false),
                    estimated_hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    is_messy = table.Column<bool>(type: "boolean", nullable: false),
                    materials_needed = table.Column<string>(type: "jsonb", nullable: false),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    project_area = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    readiness_status = table.Column<string>(type: "text", nullable: false),
                    required_tools = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_diy_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_diy_profile_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_diy_profile_activity_id",
                schema: "public",
                table: "activity_diy_profile",
                column: "activity_id",
                unique: true);
        }
    }
}
