using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <summary>
    /// Brings the two tracker pattern-mapping FKs into line with the rest of the Activity FK family:
    /// many-to-one instead of one-to-one, and <c>ON DELETE CASCADE</c> instead of <c>NO ACTION</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were <c>HasOne(...).WithOne()</c>, which put a <b>unique</b> index on <c>activity_id</c> —
    /// so a user could not map both <c>code.exe</c> and <c>rider64.exe</c> onto "Programming"; the second
    /// POST failed with 23505 surfacing as a generic 409. The domain key is the pattern, which already has
    /// its own composite unique index per user.
    /// </para>
    /// <para>
    /// And both were optional with no explicit <c>OnDelete</c>, so EF's <c>ClientSetNull</c> default left
    /// the database constraint at <c>NO ACTION</c>: these were the solution's only two activity references
    /// that did not cascade, and <c>DELETE /api/activity/{id}</c> on a mapped activity answered 409 with a
    /// message naming no entity. <c>SET NULL</c> is not an option — <c>CK_Tracker*_TargetRequired</c>
    /// requires exactly one of ignored / activity / role-or-category, so a blanked mapping fails the check
    /// rather than surviving as an unmapped rule.
    /// </para>
    /// <para>
    /// ⚠ Hand-edited after generation, in two ways EF cannot emit:
    /// the unique index is dropped and rebuilt <em>before</em> the FK swap (the other order leaves a window
    /// where a unique index still caps a relationship the model no longer thinks is 1:1), and the FK is
    /// added <c>NOT VALID</c> then validated separately so the rewrite takes <c>ACCESS EXCLUSIVE</c> only
    /// for the catalogue update and does the row scan under <c>SHARE UPDATE EXCLUSIVE</c>. The replacement
    /// indexes are built <c>CONCURRENTLY</c>, which is why those statements carry
    /// <c>suppressTransaction: true</c> — EF wraps a migration in one transaction and <c>CONCURRENTLY</c>
    /// cannot run inside one.
    /// </para>
    /// </remarks>
    public partial class TrackerPatternMappingActivityFkToManyToOneCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The unique indexes go first — they are the half that caps the relationship at 1:1.
            migrationBuilder.DropIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern");

            migrationBuilder.DropIndex(
                name: "ix_tracker_android_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern");

            // 2. Rebuilt non-unique. Not strictly needed to satisfy the FK — Postgres does not require an
            //    index on the child side — but TrackerMappingActivityReferenceSource.ReferencingActivityIds
            //    scans this column on every activity-grid page.
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "ix_tracker_desktop_mapping_by_pattern_activity_id"
                    ON "public"."tracker_desktop_mapping_by_pattern" ("activity_id");
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY "ix_tracker_android_mapping_by_pattern_activity_id"
                    ON "public"."tracker_android_mapping_by_pattern" ("activity_id");
                """,
                suppressTransaction: true);

            // 3. The FK swap. Same constraint names in and out — the configurations now pin them with
            //    HasConstraintName, so a later reshape of the relationship cannot silently rename them
            //    into a free DROP + ADD pair.
            migrationBuilder.DropForeignKey(
                name: "fk_tracker_desktop_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern");

            migrationBuilder.DropForeignKey(
                name: "fk_tracker_android_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern");

            migrationBuilder.Sql(
                """
                ALTER TABLE "public"."tracker_desktop_mapping_by_pattern"
                    ADD CONSTRAINT "fk_tracker_desktop_mapping_by_pattern_activities_activity_id"
                    FOREIGN KEY ("activity_id") REFERENCES "public"."activity" ("id") ON DELETE CASCADE
                    NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "public"."tracker_android_mapping_by_pattern"
                    ADD CONSTRAINT "fk_tracker_android_mapping_by_pattern_activities_activity_id"
                    FOREIGN KEY ("activity_id") REFERENCES "public"."activity" ("id") ON DELETE CASCADE
                    NOT VALID;
                """);

            // 4. The validating scan, in its own transaction — hence suppressTransaction here too, and it
            //    is the whole point of the split: left inside the block above, the ACCESS EXCLUSIVE lock
            //    the DROP/ADD took would simply be held until COMMIT and cover the scan as well. On its
            //    own it takes only SHARE UPDATE EXCLUSIVE, so readers and writers keep running.
            migrationBuilder.Sql(
                """
                ALTER TABLE "public"."tracker_desktop_mapping_by_pattern"
                    VALIDATE CONSTRAINT "fk_tracker_desktop_mapping_by_pattern_activities_activity_id";
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "public"."tracker_android_mapping_by_pattern"
                    VALIDATE CONSTRAINT "fk_tracker_android_mapping_by_pattern_activities_activity_id";
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// ⚠ The unique indexes cannot be rebuilt if any activity has picked up a second mapping in the
        /// meantime — which is the whole point of the Up. Rolling back therefore needs the duplicates
        /// resolved by hand first; there is no safe automatic choice of which pattern rule to destroy.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tracker_desktop_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern");

            migrationBuilder.DropForeignKey(
                name: "fk_tracker_android_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern");

            migrationBuilder.DropIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern");

            migrationBuilder.DropIndex(
                name: "ix_tracker_android_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern");

            migrationBuilder.CreateIndex(
                name: "ix_tracker_desktop_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracker_android_mapping_by_pattern_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "activity_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tracker_desktop_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_desktop_mapping_by_pattern",
                column: "activity_id",
                principalSchema: "public",
                principalTable: "activity",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tracker_android_mapping_by_pattern_activities_activity_id",
                schema: "public",
                table: "tracker_android_mapping_by_pattern",
                column: "activity_id",
                principalSchema: "public",
                principalTable: "activity",
                principalColumn: "id");
        }
    }
}
