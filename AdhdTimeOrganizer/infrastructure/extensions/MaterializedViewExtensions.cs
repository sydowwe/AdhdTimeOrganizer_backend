using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.extensions;

public static class MaterializedViewExtensions
{
    public static void RefreshAttendanceView(this AppCommandDbContext context)
    {
        context.Database.ExecuteSqlRaw("""
                                       REFRESH MATERIALIZED VIEW CONCURRENTLY "public"."materialized_view_attendance_by_days" 
                                       """);
    }
}