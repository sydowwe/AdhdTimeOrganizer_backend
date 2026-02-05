using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.timer.pomodoroTimerPreset.query;

public class GetAllPomodoroTimerPresetEndpoint(
    AppDbContext dbContext,
    PomodoroTimerPresetMapper mapper)
    : BaseGetAllEndpoint<PomodoroTimerPreset, PomodoroTimerPresetResponse, PomodoroTimerPresetMapper>(dbContext, mapper)
{
    protected override IQueryable<PomodoroTimerPreset> WithIncludes(IQueryable<PomodoroTimerPreset> query)
    {
        return query.Include(t => t.FocusActivity)
                    .Include(t => t.RestActivity);
    }
}