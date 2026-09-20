using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarHolidayKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "holiday_key",
                schema: "planning",
                table: "calendar",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Rows are written once (CalendarDayFactory) and never re-materialised, so every calendar day that
            // already exists would otherwise carry a Slovak name and no key forever. The name is the only
            // thing on the row that identifies the holiday, and this is the Slovak table as it stood when
            // those rows were written — keep the two in step with HolidayCalendar.cs. Schema-qualified on
            // purpose: hand-written SQL does not get the module schema applied for it.
            migrationBuilder.Sql("""
                UPDATE planning.calendar SET holiday_key = CASE holiday_name
                    WHEN 'Deň vzniku Slovenskej republiky / Nový rok' THEN 'NEW_YEAR'
                    WHEN 'Zjavenie Pána (Traja králi)'                THEN 'EPIPHANY'
                    WHEN 'Veľký piatok'                                THEN 'GOOD_FRIDAY'
                    WHEN 'Veľkonočný pondelok'                         THEN 'EASTER_MONDAY'
                    WHEN 'Sviatok práce'                               THEN 'LABOUR_DAY'
                    WHEN 'Deň víťazstva nad fašizmom'                  THEN 'VICTORY_DAY'
                    WHEN 'Sviatok svätého Cyrila a Metoda'             THEN 'CYRIL_AND_METHODIUS'
                    WHEN 'Výročie SNP'                                 THEN 'SNP_ANNIVERSARY'
                    WHEN 'Deň Ústavy Slovenskej republiky'             THEN 'CONSTITUTION_DAY'
                    WHEN 'Sedembolestná Panna Mária'                   THEN 'OUR_LADY_OF_SORROWS'
                    WHEN 'Sviatok Všetkých svätých'                    THEN 'ALL_SAINTS'
                    WHEN 'Deň boja za slobodu a demokraciu'            THEN 'FREEDOM_AND_DEMOCRACY_DAY'
                    WHEN 'Štedrý deň'                                  THEN 'CHRISTMAS_EVE'
                    WHEN 'Prvý sviatok vianočný'                       THEN 'CHRISTMAS_DAY'
                    WHEN 'Druhý sviatok vianočný'                      THEN 'ST_STEPHENS_DAY'
                END
                WHERE holiday_name IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "holiday_key",
                schema: "planning",
                table: "calendar");
        }
    }
}
