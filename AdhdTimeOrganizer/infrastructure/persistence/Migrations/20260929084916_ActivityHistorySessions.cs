using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityHistorySessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phase",
                schema: "history",
                table: "activity_history",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "session_id",
                schema: "history",
                table: "activity_history",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activity_history_session",
                schema: "history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    planned_focus_periods = table.Column<int>(type: "integer", nullable: true),
                    completed_focus_periods = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_history_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_history_session_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "user",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_session_id",
                schema: "history",
                table: "activity_history",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_session_user_id",
                schema: "history",
                table: "activity_history_session",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_history_activity_history_session_session_id",
                schema: "history",
                table: "activity_history",
                column: "session_id",
                principalSchema: "history",
                principalTable: "activity_history_session",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_history_activity_history_session_session_id",
                schema: "history",
                table: "activity_history");

            migrationBuilder.DropTable(
                name: "activity_history_session",
                schema: "history");

            migrationBuilder.DropIndex(
                name: "ix_activity_history_session_id",
                schema: "history",
                table: "activity_history");

            migrationBuilder.DropColumn(
                name: "phase",
                schema: "history",
                table: "activity_history");

            migrationBuilder.DropColumn(
                name: "session_id",
                schema: "history",
                table: "activity_history");
        }
    }
}
