using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "user",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    current_locale = table.Column<string>(type: "text", nullable: false),
                    timezone = table.Column<string>(type: "text", nullable: false),
                    google_o_auth_user_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    has_extension_access = table.Column<bool>(type: "boolean", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_role",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    role_level = table.Column<int>(type: "integer", nullable: false),
                    is_assignable = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_category",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_category", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_category_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_role",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_role_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "android_session_data",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    package_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    app_label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    session_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    session_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_android_session_data", x => x.id);
                    table.ForeignKey(
                        name: "fk_android_session_data_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_type = table.Column<string>(type: "text", nullable: false),
                    holiday_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    wake_up_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    bed_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    applied_template_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    weather = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "desktop_activity_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    record_date = table.Column<DateOnly>(type: "date", nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    process_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    window_title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    executable_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    is_fullscreen = table.Column<bool>(type: "boolean", nullable: false),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    background_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_playing_sound = table.Column<bool>(type: "boolean", nullable: false),
                    active_monitor = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desktop_activity_entry", x => new { x.id, x.record_date });
                    table.ForeignKey(
                        name: "fk_desktop_activity_entry_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    stay_logged_in = table.Column<bool>(type: "boolean", nullable: false),
                    is_extension_client = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    auth_method = table.Column<int>(type: "integer", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_token_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routine_time_period",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    length_in_days = table.Column<int>(type: "integer", nullable: false),
                    reset_anchor_day = table.Column<int>(type: "integer", nullable: false),
                    streak = table.Column<int>(type: "integer", nullable: false),
                    best_streak = table.Column<int>(type: "integer", nullable: false),
                    streak_threshold = table.Column<int>(type: "integer", nullable: false),
                    streak_grace_days = table.Column<int>(type: "integer", nullable: false),
                    last_reset_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    streak_grace_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    history_depth = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_time_period", x => x.id);
                    table.CheckConstraint("ck_routine_time_period_best_streak_non_negative", "\"best_streak\" >= 0");
                    table.CheckConstraint("ck_routine_time_period_history_depth_range", "\"history_depth\" >= 1 AND \"history_depth\" <= 100");
                    table.CheckConstraint("ck_routine_time_period_length_in_days_range", "\"length_in_days\" >= 1 AND \"length_in_days\" <= 365");
                    table.CheckConstraint("ck_routine_time_period_reset_anchor_day_range", "(\"length_in_days\" <= 7 OR \"length_in_days\" % 7 = 0 AND \"reset_anchor_day\" BETWEEN 1 AND 7) OR (\"length_in_days\" > 7 AND \"length_in_days\" % 7 <> 0 AND \"reset_anchor_day\" BETWEEN 1 AND 30)");
                    table.CheckConstraint("ck_routine_time_period_streak_grace_days_range", "\"streak_grace_days\" >= 0 AND \"streak_grace_days\" <= \"length_in_days\" - 1");
                    table.CheckConstraint("ck_routine_time_period_streak_non_negative", "\"streak\" >= 0");
                    table.CheckConstraint("ck_routine_time_period_streak_threshold_range", "\"streak_threshold\" >= 1 AND \"streak_threshold\" <= 100");
                    table.ForeignKey(
                        name: "fk_routine_time_period_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_importance",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    importance = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_importance", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_importance_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_planner_day_template",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    default_wake_up_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    default_bed_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    suggested_for_day_type = table.Column<int>(type: "integer", nullable: false),
                    scheduled_days = table.Column<string>(type: "text", nullable: false),
                    suggested_location = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "text", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_planner_day_template", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_planner_day_template_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_priority",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_priority", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_priority_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todo_list_category",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list_category", x => x.id);
                    table.ForeignKey(
                        name: "fk_todo_list_category_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claim",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claim_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_login",
                schema: "public",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_login_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                schema: "public",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_token_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_extension_activity_entry",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    record_date = table.Column<DateOnly>(type: "date", nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    active_seconds = table.Column<int>(type: "integer", nullable: false),
                    background_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_final = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_extension_activity_entry", x => new { x.id, x.record_date });
                    table.ForeignKey(
                        name: "fk_web_extension_activity_entry_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user__role",
                schema: "public",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user__role", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user__role_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "user_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user__role_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role_claim",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_role_claim_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "user_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    is_unavoidable = table.Column<bool>(type: "boolean", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_activity_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "public",
                        principalTable: "activity_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_activity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "activity_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routine_period_completions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    time_period_id = table.Column<long>(type: "bigint", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_count = table.Column<int>(type: "integer", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_period_completions", x => x.id);
                    table.ForeignKey(
                        name: "fk_routine_period_completions_routine_time_periods_time_period",
                        column: x => x.time_period_id,
                        principalSchema: "public",
                        principalTable: "routine_time_period",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todo_list",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    icon = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list", x => x.id);
                    table.ForeignKey(
                        name: "fk_todo_list_todo_list_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "public",
                        principalTable: "todo_list_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_todo_list_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "activity_history",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    start_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    length = table.Column<int>(type: "integer", nullable: false),
                    end_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_history_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activity_history_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alarm",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    start_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
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
                name: "routine_todo_list",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    time_period_id = table.Column<long>(type: "bigint", nullable: false),
                    last_reset_date = table.Column<DateOnly>(type: "date", nullable: true),
                    streak = table.Column<int>(type: "integer", nullable: false),
                    best_streak = table.Column<int>(type: "integer", nullable: false),
                    last_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    done_count = table.Column<int>(type: "integer", nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    suggested_time = table.Column<int>(type: "integer", nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_todo_list", x => x.id);
                    table.CheckConstraint("CK_RoutineTodoList_BestStreak_NonNegative", "\"best_streak\" >= 0");
                    table.CheckConstraint("CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
                    table.CheckConstraint("CK_RoutineTodoList_DoneCount_Min", "done_count IS NULL OR done_count >= 0");
                    table.CheckConstraint("CK_RoutineTodoList_Streak_NonNegative", "\"streak\" >= 0");
                    table.CheckConstraint("CK_RoutineTodoList_TotalCount_Range", "total_count IS NULL OR total_count >= 2 AND total_count <= 99");
                    table.ForeignKey(
                        name: "fk_routine_todo_list_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_routine_todo_list_routine_time_period_time_period_id",
                        column: x => x.time_period_id,
                        principalSchema: "public",
                        principalTable: "routine_time_period",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_routine_todo_list_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_planner_task",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    template_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_background = table.Column<bool>(type: "boolean", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    importance_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_planner_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_planner_task_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_planner_task_task_importance_importance_id",
                        column: x => x.importance_id,
                        principalSchema: "public",
                        principalTable: "task_importance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_template_planner_task_task_planner_day_template_template_id",
                        column: x => x.template_id,
                        principalSchema: "public",
                        principalTable: "task_planner_day_template",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_planner_task_user_user_id",
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

            migrationBuilder.CreateTable(
                name: "tracker_desktop_mapping_by_pattern",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    process_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    process_name_match_type = table.Column<int>(type: "integer", nullable: true),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    product_name_match_type = table.Column<int>(type: "integer", nullable: true),
                    window_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    window_title_match_type = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_ignored = table.Column<bool>(type: "boolean", nullable: true),
                    activity_id = table.Column<long>(type: "bigint", nullable: true),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracker_desktop_mapping_by_pattern", x => x.id);
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_IsIgnoredOnlyTrue", "\"is_ignored\" IS NULL OR \"is_ignored\" = TRUE");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_ProcessNameMatchType", "process_name IS NULL OR process_name_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_ProductNameMatchType", "product_name IS NULL OR product_name_match_type IS NOT NULL");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_TargetRequired", "(\r\n  CASE WHEN \"is_ignored\" = TRUE THEN 1 ELSE 0 END +\r\n  CASE WHEN \"activity_id\" IS NOT NULL THEN 1 ELSE 0 END +\r\n  CASE WHEN \"role_id\" IS NOT NULL OR \"category_id\" IS NOT NULL THEN 1 ELSE 0 END\r\n) = 1");
                    table.CheckConstraint("CK_TrackerDesktopMappingByPattern_WindowTitleMatchType", "window_title IS NULL OR window_title_match_type IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activity_categories_cate",
                        column: x => x.category_id,
                        principalSchema: "public",
                        principalTable: "activity_category",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_activity_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "activity_role",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracker_desktop_mapping_by_pattern_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "todo_list_item",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    task_priority_id = table.Column<long>(type: "bigint", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    todo_list_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    done_count = table.Column<int>(type: "integer", nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    suggested_time = table.Column<int>(type: "integer", nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list_item", x => x.id);
                    table.CheckConstraint("CK_TodoListItem_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
                    table.CheckConstraint("CK_TodoListItem_DoneCount_Min", "done_count IS NULL OR done_count >= 0");
                    table.CheckConstraint("CK_TodoListItem_TotalCount_Range", "total_count IS NULL OR total_count >= 2 AND total_count <= 99");
                    table.ForeignKey(
                        name: "fk_todo_list_item_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_todo_list_item_task_priority_task_priority_id",
                        column: x => x.task_priority_id,
                        principalSchema: "public",
                        principalTable: "task_priority",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_todo_list_item_todo_list_todo_list_id",
                        column: x => x.todo_list_id,
                        principalSchema: "public",
                        principalTable: "todo_list",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_todo_list_item_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planner_task",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    status = table.Column<int>(type: "integer", nullable: false),
                    actual_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    actual_end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    skip_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_from_template = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source_template_task_id = table.Column<long>(type: "bigint", nullable: true),
                    calendar_id = table.Column<long>(type: "bigint", nullable: false),
                    todolist_item_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_background = table.Column<bool>(type: "boolean", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    importance_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planner_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_planner_task_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_planner_task_calendar_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "public",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_planner_task_task_importances_importance_id",
                        column: x => x.importance_id,
                        principalSchema: "public",
                        principalTable: "task_importance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_planner_task_todo_list_items_todolist_item_id",
                        column: x => x.todolist_item_id,
                        principalSchema: "public",
                        principalTable: "todo_list_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_planner_task_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_category_id",
                schema: "public",
                table: "activity",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_role_id",
                schema: "public",
                table: "activity",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_category_id",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_name",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_user_id_role_id",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_category_user_id_name",
                schema: "public",
                table: "activity_category",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_activity_id",
                schema: "public",
                table: "activity_history",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_history_user_id_activity_id_start_timestamp",
                schema: "public",
                table: "activity_history",
                columns: new[] { "user_id", "activity_id", "start_timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_activity_role_user_id_name",
                schema: "public",
                table: "activity_role",
                columns: new[] { "user_id", "name" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_device_id_package_name_session",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "device_id", "package_name", "session_start_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_package_name",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "package_name" });

            migrationBuilder.CreateIndex(
                name: "ix_android_session_data_user_id_session_start_utc",
                schema: "public",
                table: "android_session_data",
                columns: new[] { "user_id", "session_start_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_day_type",
                schema: "public",
                table: "calendar",
                column: "day_type");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_user_id_date",
                schema: "public",
                table: "calendar",
                columns: new[] { "user_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id_window_start",
                schema: "public",
                table: "desktop_activity_entry",
                columns: new[] { "user_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_desktop_activity_entry_user_id_window_start_record_date_pro",
                schema: "public",
                table: "desktop_activity_entry",
                columns: new[] { "user_id", "window_start", "record_date", "process_name", "window_title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_activity_id",
                schema: "public",
                table: "planner_task",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_calendar_id",
                schema: "public",
                table: "planner_task",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_importance_id",
                schema: "public",
                table: "planner_task",
                column: "importance_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_status",
                schema: "public",
                table: "planner_task",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_todolist_item_id",
                schema: "public",
                table: "planner_task",
                column: "todolist_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_user_id_calendar_id_start_time",
                schema: "public",
                table: "planner_task",
                columns: new[] { "user_id", "calendar_id", "start_time" });

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
                name: "ix_refresh_token_expires_at",
                schema: "public",
                table: "refresh_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_token_hash",
                schema: "public",
                table: "refresh_token",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_revoked",
                schema: "public",
                table: "refresh_token",
                columns: new[] { "user_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "ix_routine_period_completions_time_period_id_period_start",
                schema: "public",
                table: "routine_period_completions",
                columns: new[] { "time_period_id", "period_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_routine_time_period_user_id_length_in_days",
                schema: "public",
                table: "routine_time_period",
                columns: new[] { "user_id", "length_in_days" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_routine_time_period_user_id_text",
                schema: "public",
                table: "routine_time_period",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_routine_todo_list_activity_id",
                schema: "public",
                table: "routine_todo_list",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_routine_todo_list_time_period_id",
                schema: "public",
                table: "routine_todo_list",
                column: "time_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_routine_todo_list_user_id_time_period_id",
                schema: "public",
                table: "routine_todo_list",
                columns: new[] { "user_id", "time_period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_routine_todo_list_user_id_time_period_id_activity_id",
                schema: "public",
                table: "routine_todo_list",
                columns: new[] { "user_id", "time_period_id", "activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_importance",
                schema: "public",
                table: "task_importance",
                column: "importance");

            migrationBuilder.CreateIndex(
                name: "ix_task_importance_user_id_importance",
                schema: "public",
                table: "task_importance",
                columns: new[] { "user_id", "importance" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_planner_day_template_suggested_for_day_type",
                schema: "public",
                table: "task_planner_day_template",
                column: "suggested_for_day_type");

            migrationBuilder.CreateIndex(
                name: "ix_task_planner_day_template_user_id_name",
                schema: "public",
                table: "task_planner_day_template",
                columns: new[] { "user_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_task_priority_priority",
                schema: "public",
                table: "task_priority",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_task_priority_user_id_priority",
                schema: "public",
                table: "task_priority",
                columns: new[] { "user_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_priority_user_id_text",
                schema: "public",
                table: "task_priority",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_activity_id",
                schema: "public",
                table: "template_planner_task",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_importance_id",
                schema: "public",
                table: "template_planner_task",
                column: "importance_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_template_id_start_time",
                schema: "public",
                table: "template_planner_task",
                columns: new[] { "template_id", "start_time" });

            migrationBuilder.CreateIndex(
                name: "ix_template_planner_task_user_id",
                schema: "public",
                table: "template_planner_task",
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

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_category_id",
                schema: "public",
                table: "todo_list",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_name",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_category_user_id_name",
                schema: "public",
                table: "todo_list_category",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_activity_id",
                schema: "public",
                table: "todo_list_item",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_task_priority_id",
                schema: "public",
                table: "todo_list_item",
                column: "task_priority_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                column: "todo_list_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_activity_id_todo_list_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "activity_id", "todo_list_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_item_user_id_task_priority_id",
                schema: "public",
                table: "todo_list_item",
                columns: new[] { "user_id", "task_priority_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_category_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_role_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_user_id_process_name_pro",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                columns: new[] { "user_id", "process_name", "product_name", "window_title" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "public",
                table: "user",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "public",
                table: "user",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user__role_role_id",
                schema: "public",
                table: "user__role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claim_user_id",
                schema: "public",
                table: "user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_login_user_id",
                schema: "public",
                table: "user_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "public",
                table: "user_role",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_role_claim_role_id",
                schema: "public",
                table: "user_role_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_activity_entry_user_id_window_start",
                schema: "public",
                table: "web_extension_activity_entry",
                columns: new[] { "user_id", "window_start" });

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_activity_entry_user_id_window_start_domain_re",
                schema: "public",
                table: "web_extension_activity_entry",
                columns: new[] { "user_id", "window_start", "domain", "record_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_history",
                schema: "public");

            migrationBuilder.DropTable(
                name: "alarm",
                schema: "public");

            migrationBuilder.DropTable(
                name: "android_session_data",
                schema: "public");

            migrationBuilder.DropTable(
                name: "desktop_activity_entry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "planner_task",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pomodoro_timer_preset",
                schema: "public");

            migrationBuilder.DropTable(
                name: "refresh_token",
                schema: "public");

            migrationBuilder.DropTable(
                name: "routine_period_completions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "routine_todo_list",
                schema: "public");

            migrationBuilder.DropTable(
                name: "template_planner_task",
                schema: "public");

            migrationBuilder.DropTable(
                name: "timer_preset",
                schema: "public");

            migrationBuilder.DropTable(
                name: "tracker_desktop_mapping_by_pattern",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user__role",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_claim",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_login",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_role_claim",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_token",
                schema: "public");

            migrationBuilder.DropTable(
                name: "web_extension_activity_entry",
                schema: "public");

            migrationBuilder.DropTable(
                name: "calendar",
                schema: "public");

            migrationBuilder.DropTable(
                name: "todo_list_item",
                schema: "public");

            migrationBuilder.DropTable(
                name: "routine_time_period",
                schema: "public");

            migrationBuilder.DropTable(
                name: "task_importance",
                schema: "public");

            migrationBuilder.DropTable(
                name: "task_planner_day_template",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity",
                schema: "public");

            migrationBuilder.DropTable(
                name: "task_priority",
                schema: "public");

            migrationBuilder.DropTable(
                name: "todo_list",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_category",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_role",
                schema: "public");

            migrationBuilder.DropTable(
                name: "todo_list_category",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user",
                schema: "public");
        }
    }
}
