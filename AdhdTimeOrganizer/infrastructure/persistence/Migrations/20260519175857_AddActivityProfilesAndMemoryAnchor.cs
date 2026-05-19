using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityProfilesAndMemoryAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_diy_profile",
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
                    table.PrimaryKey("pk_activity_diy_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_diy_profile_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_expected_cost_tier",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_expected_cost_tier", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_expected_cost_tier_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_experience_type",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_experience_type", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_experience_type_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_location_type",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_location_type", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_location_type_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_weather_dependency",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_weather_dependency", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_weather_dependency_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memory_anchor",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    anchor_month = table.Column<int>(type: "integer", nullable: false),
                    anchor_year = table.Column<int>(type: "integer", nullable: false),
                    highlight_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memory_anchor", x => x.id);
                    table.CheckConstraint("ck_memory_anchor_month", "anchor_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_memory_anchor_rating", "rating BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "fk_memory_anchor_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memory_anchor_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_bucket_list_profile",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    experience_type_id = table.Column<long>(type: "bigint", nullable: false),
                    comfort_zone_step = table.Column<int>(type: "integer", nullable: false),
                    requires_travel = table.Column<bool>(type: "boolean", nullable: false),
                    financial_goal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    inspiration_source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_bucket_list_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_bucket_list_profile_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_bucket_list_profile_activity_experience_types_expe",
                        column: x => x.experience_type_id,
                        principalSchema: "public",
                        principalTable: "activity_experience_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_backlog_profile",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type_id = table.Column<long>(type: "bigint", nullable: false),
                    weather_dependency_id = table.Column<long>(type: "bigint", nullable: false),
                    energy_level = table.Column<string>(type: "text", nullable: false),
                    effort_type = table.Column<string>(type: "text", nullable: true),
                    min_participants = table.Column<int>(type: "integer", nullable: false),
                    max_participants = table.Column<int>(type: "integer", nullable: true),
                    expected_cost_tier_id = table.Column<long>(type: "bigint", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_repeatable = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_backlog_profile", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_backlog_profile_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_backlog_profile_activity_expected_cost_tiers_expec",
                        column: x => x.expected_cost_tier_id,
                        principalSchema: "public",
                        principalTable: "activity_expected_cost_tier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_backlog_profile_activity_location_types_location_t",
                        column: x => x.location_type_id,
                        principalSchema: "public",
                        principalTable: "activity_location_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_backlog_profile_activity_weather_dependencies_weat",
                        column: x => x.weather_dependency_id,
                        principalSchema: "public",
                        principalTable: "activity_weather_dependency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_backlog_profile_activity_id",
                schema: "public",
                table: "activity_backlog_profile",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_backlog_profile_expected_cost_tier_id",
                schema: "public",
                table: "activity_backlog_profile",
                column: "expected_cost_tier_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_backlog_profile_location_type_id",
                schema: "public",
                table: "activity_backlog_profile",
                column: "location_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_backlog_profile_weather_dependency_id",
                schema: "public",
                table: "activity_backlog_profile",
                column: "weather_dependency_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_bucket_list_profile_activity_id",
                schema: "public",
                table: "activity_bucket_list_profile",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_bucket_list_profile_experience_type_id",
                schema: "public",
                table: "activity_bucket_list_profile",
                column: "experience_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_diy_profile_activity_id",
                schema: "public",
                table: "activity_diy_profile",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_expected_cost_tier_user_id_text",
                schema: "public",
                table: "activity_expected_cost_tier",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_experience_type_user_id_text",
                schema: "public",
                table: "activity_experience_type",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_location_type_user_id_text",
                schema: "public",
                table: "activity_location_type",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_weather_dependency_user_id_text",
                schema: "public",
                table: "activity_weather_dependency",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memory_anchor_activity_id_anchor_year_anchor_month",
                schema: "public",
                table: "memory_anchor",
                columns: new[] { "activity_id", "anchor_year", "anchor_month" });

            migrationBuilder.CreateIndex(
                name: "ix_memory_anchor_user_id",
                schema: "public",
                table: "memory_anchor",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_backlog_profile",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_bucket_list_profile",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_diy_profile",
                schema: "public");

            migrationBuilder.DropTable(
                name: "memory_anchor",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_expected_cost_tier",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_location_type",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_weather_dependency",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_experience_type",
                schema: "public");
        }
    }
}
