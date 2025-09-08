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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
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
                    icon = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
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
                name: "routine_time_period",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    length_in_days = table.Column<int>(type: "integer", nullable: false),
                    is_hidden_in_view = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_time_period", x => x.id);
                    table.ForeignKey(
                        name: "fk_routine_time_period_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_urgency",
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
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_urgency", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_urgency_user_user_id",
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
                name: "planner_task",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    start_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    minute_length = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
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
                        name: "fk_planner_task_user_user_id",
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
                    done_count = table.Column<int>(type: "integer", nullable: true),
                    total_count = table.Column<int>(type: "integer", nullable: true),
                    time_period_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routine_todo_list", x => x.id);
                    table.CheckConstraint("CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
                    table.CheckConstraint("CK_RoutineTodoList_DoneCount_Min", "done_count >= 0");
                    table.CheckConstraint("CK_RoutineTodoList_TotalCount_Range", "total_count >= 2 AND total_count <= 99");
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
                name: "todo_list",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    done_count = table.Column<int>(type: "integer", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    task_urgency_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_todo_list", x => x.id);
                    table.CheckConstraint("CK_TodoList_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
                    table.CheckConstraint("CK_TodoList_DoneCount_Min", "done_count >= 0");
                    table.CheckConstraint("CK_TodoList_TotalCount_Range", "total_count >= 2 AND total_count <= 99");
                    table.ForeignKey(
                        name: "fk_todo_list_activity_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_todo_list_task_urgency_task_urgency_id",
                        column: x => x.task_urgency_id,
                        principalSchema: "public",
                        principalTable: "task_urgency",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_todo_list_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_extension_data",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    domain = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    duration = table.Column<int>(type: "integer", nullable: false),
                    start_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    modified_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    activity_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_extension_data", x => x.id);
                    table.ForeignKey(
                        name: "fk_web_extension_data_activities_activity_id",
                        column: x => x.activity_id,
                        principalSchema: "public",
                        principalTable: "activity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_web_extension_data_user_user_id",
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
                name: "ix_activity_user_id_name",
                schema: "public",
                table: "activity",
                columns: new[] { "user_id", "name" },
                unique: true);

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
                name: "ix_planner_task_activity_id",
                schema: "public",
                table: "planner_task",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_planner_task_user_id_start_timestamp",
                schema: "public",
                table: "planner_task",
                columns: new[] { "user_id", "start_timestamp" },
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
                name: "ix_task_urgency_user_id_priority",
                schema: "public",
                table: "task_urgency",
                columns: new[] { "user_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_urgency_user_id_text",
                schema: "public",
                table: "task_urgency",
                columns: new[] { "user_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_activity_id",
                schema: "public",
                table: "todo_list",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_task_urgency_id",
                schema: "public",
                table: "todo_list",
                column: "task_urgency_id");

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_activity_id",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_todo_list_user_id_task_urgency_id",
                schema: "public",
                table: "todo_list",
                columns: new[] { "user_id", "task_urgency_id" });

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
                name: "ix_web_extension_data_activity_id",
                schema: "public",
                table: "web_extension_data",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_web_extension_data_user_id_activity_id_start_timestamp",
                schema: "public",
                table: "web_extension_data",
                columns: new[] { "user_id", "activity_id", "start_timestamp" },
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
                name: "planner_task",
                schema: "public");

            migrationBuilder.DropTable(
                name: "routine_todo_list",
                schema: "public");

            migrationBuilder.DropTable(
                name: "todo_list",
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
                name: "web_extension_data",
                schema: "public");

            migrationBuilder.DropTable(
                name: "routine_time_period",
                schema: "public");

            migrationBuilder.DropTable(
                name: "task_urgency",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_role",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_category",
                schema: "public");

            migrationBuilder.DropTable(
                name: "activity_role",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user",
                schema: "public");
        }
    }
}
