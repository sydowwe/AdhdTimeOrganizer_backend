using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeisureWeatherSignal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "weather_location",
                schema: "public",
                table: "user",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                schema: "public",
                table: "activity_weather_dependency",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Backfill for users who already hold the four seeded rows. Without it those rows fall back to
            // WeatherDependencyCodes.Infer guessing their own English labels back — which works today and stops
            // working the moment anyone renames one, which is the entire reason the column exists.
            //
            // Matching on the seeded English text is safe precisely because the seeder writes those four strings
            // in every locale; a row the user has already renamed does not match, and is left to Infer, exactly
            // as a row they invented themselves is.
            migrationBuilder.Sql("""
                UPDATE public.activity_weather_dependency SET code = CASE text
                    WHEN 'None' THEN 'none' WHEN 'Sunny' THEN 'sunny'
                    WHEN 'Dry' THEN 'dry'   WHEN 'Snow'  THEN 'snow' END
                WHERE code IS NULL AND text IN ('None', 'Sunny', 'Dry', 'Snow');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "weather_location",
                schema: "public",
                table: "user");

            migrationBuilder.DropColumn(
                name: "code",
                schema: "public",
                table: "activity_weather_dependency");
        }
    }
}
