using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class timer_pomodoroTimerPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pomodoro_timer_preset",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    focus_duration = table.Column<int>(type: "integer", nullable: false),
                    short_break_duration = table.Column<int>(type: "integer", nullable: false),
                    long_break_duration = table.Column<int>(type: "integer", nullable: false),
                    focus_period_in_cycle_count = table.Column<int>(type: "integer", nullable: false),
                    number_of_cycles = table.Column<int>(type: "integer", nullable: false),
                    focus_activity_id = table.Column<long>(type: "bigint", nullable: true),
                    rest_activity_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pomodoro_timer_preset", x => x.id);
                    table.ForeignKey(
                        name: "fk_pomodoro_timer_preset_activity_focus_activity_id",
                        column: x => x.focus_activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pomodoro_timer_preset_activity_rest_activity_id",
                        column: x => x.rest_activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pomodoro_timer_preset_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "timer_preset",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    duration = table.Column<int>(type: "integer", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timer_preset", x => x.id);
                    table.ForeignKey(
                        name: "fk_timer_preset_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_timer_preset_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pomodoro_timer_preset_focus_activity_id",
                schema: "public",
                table: "pomodoro_timer_preset",
                column: "focus_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_pomodoro_timer_preset_rest_activity_id",
                schema: "public",
                table: "pomodoro_timer_preset",
                column: "rest_activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_pomodoro_timer_preset_user_id",
                schema: "public",
                table: "pomodoro_timer_preset",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_timer_preset_activity_id",
                schema: "public",
                table: "timer_preset",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_timer_preset_user_id",
                schema: "public",
                table: "timer_preset",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pomodoro_timer_preset",
                schema: "public");

            migrationBuilder.DropTable(
                name: "timer_preset",
                schema: "public");
        }
    }
}
