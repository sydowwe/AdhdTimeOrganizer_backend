using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusinessAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "business_audit_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    entity_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    entity_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_audit_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_log_correlation_id",
                schema: "audit",
                table: "business_audit_log",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_log_entity_name_entity_id",
                schema: "audit",
                table: "business_audit_log",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_log_event_type",
                schema: "audit",
                table: "business_audit_log",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_log_timestamp",
                schema: "audit",
                table: "business_audit_log",
                column: "timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_business_audit_log_user_id",
                schema: "audit",
                table: "business_audit_log",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_audit_log",
                schema: "audit");
        }
    }
}
