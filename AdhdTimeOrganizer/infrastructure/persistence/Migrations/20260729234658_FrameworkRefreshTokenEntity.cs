using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdhdTimeOrganizer.infrastructure.persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Swaps the portal's own RefreshToken entity for Sydowwe.Framework's. The table, columns and
    /// indexes are identical, so the only work is a data fix: auth_method is stored as an int, and the
    /// portal's enum was (Password=0, Google=1) while the framework's is (Password=0, Microsoft=1,
    /// Google=2). Existing rows holding 1 mean Google and must move to 2, or a Google session would
    /// come back claiming auth_method=Microsoft.
    /// </summary>
    public partial class FrameworkRefreshTokenEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE public.refresh_token SET auth_method = 2 WHERE auth_method = 1;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE public.refresh_token SET auth_method = 1 WHERE auth_method = 2;""");
        }
    }
}
