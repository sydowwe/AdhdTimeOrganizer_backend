using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlannerTaskCarryOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "carry_over_count",
                schema: "planning",
                table: "planner_task",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "original_date",
                schema: "planning",
                table: "planner_task",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carry_over_count",
                schema: "planning",
                table: "planner_task");

            migrationBuilder.DropColumn(
                name: "original_date",
                schema: "planning",
                table: "planner_task");
        }
    }
}
