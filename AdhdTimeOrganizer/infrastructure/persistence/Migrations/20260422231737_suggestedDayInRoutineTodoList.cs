using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class suggestedDayInRoutineTodoList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm",
                schema: "public");

            migrationBuilder.AddColumn<int>(
                name: "suggested_day",
                schema: "public",
                table: "routine_todo_list",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "window_title",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "product_name",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "executable_path",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_routine_todo_list_suggested_day_range",
                schema: "public",
                table: "routine_todo_list",
                sql: "\"suggested_day\" IS NULL OR (\"suggested_day\" BETWEEN 1 AND 30)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_routine_todo_list_suggested_day_range",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.DropColumn(
                name: "suggested_day",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.AlterColumn<string>(
                name: "window_title",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AlterColumn<string>(
                name: "product_name",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "executable_path",
                schema: "public",
                table: "desktop_activity_entry",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.CreateTable(
                name: "alarm",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    start_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alarm", x => x.id);
                    table.ForeignKey(
                        name: "fk_alarm_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alarm_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alarm_activity_id",
                schema: "public",
                table: "alarm",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_user_id_start_timestamp",
                schema: "public",
                table: "alarm",
                columns: new[] { "user_id", "start_timestamp" },
                unique: true);
        }
    }
}
