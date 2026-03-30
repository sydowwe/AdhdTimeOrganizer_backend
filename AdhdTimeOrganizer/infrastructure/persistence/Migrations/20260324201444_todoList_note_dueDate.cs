using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class todoList_note_dueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_activity_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "public",
                table: "todo_list_item",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "due_time",
                schema: "public",
                table: "todo_list_item",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "public",
                table: "todo_list_item",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "public",
                table: "routine_todo_list",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_activity_id_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "activity_id", "todo_list_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_todo_list_item_user_id_activity_id_todo_list_id",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "due_time",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "public",
                table: "todo_list_item");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "public",
                table: "routine_todo_list");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_activity_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "activity_id" },
                unique: true);
        }
    }
}
