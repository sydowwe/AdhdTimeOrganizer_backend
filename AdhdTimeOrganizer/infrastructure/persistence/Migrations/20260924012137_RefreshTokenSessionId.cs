using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing row becomes its own session. The scaffolded default was the zero GUID, which
            // would have put every pre-deploy session of every user into one "session" — revoking any of
            // them would then have revoked them all. gen_random_uuid() is volatile, so Postgres evaluates
            // it once per existing row while adding the column. A pre-deploy chain's older, already-revoked
            // links get ids of their own too, which is harmless: only the live link's id is ever listed,
            // copied forward on the next rotation, or checked.
            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                schema: "user",
                table: "refresh_token",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            // The model has no default — every insert sets the id — so drop it once the backfill is done,
            // keeping the column identical to what a fresh database gets from the snapshot.
            migrationBuilder.Sql("""ALTER TABLE "user".refresh_token ALTER COLUMN session_id DROP DEFAULT;""");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_session_id",
                schema: "user",
                table: "refresh_token",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_token_session_id",
                schema: "user",
                table: "refresh_token");

            migrationBuilder.DropColumn(
                name: "session_id",
                schema: "user",
                table: "refresh_token");
        }
    }
}
