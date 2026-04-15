using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class AndroidSessionData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_at",
                schema: "public",
                table: "android_session_data",
                newName: "modified_timestamp");

            // uuid → bigint cannot be cast automatically and window functions are not allowed
            // in USING transform expressions. A temporary sequence is used so each existing
            // UUID row gets a unique sequential bigint without requiring a table rewrite via TRUNCATE.
            migrationBuilder.Sql("""
                ALTER TABLE public.android_session_data ALTER COLUMN id DROP DEFAULT;
                CREATE SEQUENCE public.android_session_data_id_temp_seq;
                ALTER TABLE public.android_session_data ALTER COLUMN id TYPE bigint
                    USING nextval('public.android_session_data_id_temp_seq');
                DROP SEQUENCE public.android_session_data_id_temp_seq;
                CREATE SEQUENCE public.android_session_data_id_seq START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE NO CYCLE;
                SELECT setval('public.android_session_data_id_seq', COALESCE(MAX(id), 0) + 1, false)
                    FROM public.android_session_data;
                ALTER TABLE public.android_session_data ALTER COLUMN id SET DEFAULT nextval('public.android_session_data_id_seq');
                ALTER SEQUENCE public.android_session_data_id_seq OWNED BY public.android_session_data.id;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_timestamp",
                schema: "public",
                table: "android_session_data",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "public",
                table: "android_session_data",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_timestamp",
                schema: "public",
                table: "android_session_data");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "public",
                table: "android_session_data");

            migrationBuilder.RenameColumn(
                name: "modified_timestamp",
                schema: "public",
                table: "android_session_data",
                newName: "created_at");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "public",
                table: "android_session_data",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);
        }
    }
}
