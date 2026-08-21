using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.migrations
{
    /// <inheritdoc />
    public partial class AddTimerBoundaryAlarm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "timer_boundary_alarm",
                schema: "history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    boundary_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    phase = table.Column<int>(type: "integer", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: true),
                    phase_index = table.Column<int>(type: "integer", nullable: true),
                    phase_total = table.Column<int>(type: "integer", nullable: true),
                    cycle_index = table.Column<int>(type: "integer", nullable: true),
                    cycle_total = table.Column<int>(type: "integer", nullable: true),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timer_boundary_alarm", x => x.id);
                    table.ForeignKey(
                        name: "fk_timer_boundary_alarm_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "user",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_timer_boundary_alarm_boundary_at",
                schema: "history",
                table: "timer_boundary_alarm",
                column: "boundary_at");

            migrationBuilder.CreateIndex(
                name: "ix_timer_boundary_alarm_user_id",
                schema: "history",
                table: "timer_boundary_alarm",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_timer_boundary_alarm_user_id_session_id",
                schema: "history",
                table: "timer_boundary_alarm",
                columns: new[] { "user_id", "session_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "timer_boundary_alarm",
                schema: "history");
        }
    }
}
