using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class TimeAway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "time_away_id",
                schema: "planning",
                table: "calendar",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "time_away",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    template_id = table.Column<long>(type: "bigint", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_away", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_away_task_planner_day_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "planning",
                        principalTable: "task_planner_day_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_time_away_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "user",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_time_away_id",
                schema: "planning",
                table: "calendar",
                column: "time_away_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_away_template_id",
                schema: "planning",
                table: "time_away",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_away_user_id",
                schema: "planning",
                table: "time_away",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_away_user_id_start_date",
                schema: "planning",
                table: "time_away",
                columns: new[] { "user_id", "start_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_time_away_time_away_id",
                schema: "planning",
                table: "calendar",
                column: "time_away_id",
                principalSchema: "planning",
                principalTable: "time_away",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_calendar_time_away_time_away_id",
                schema: "planning",
                table: "calendar");

            migrationBuilder.DropTable(
                name: "time_away",
                schema: "planning");

            migrationBuilder.DropIndex(
                name: "ix_calendar_time_away_id",
                schema: "planning",
                table: "calendar");

            migrationBuilder.DropColumn(
                name: "time_away_id",
                schema: "planning",
                table: "calendar");
        }
    }
}
