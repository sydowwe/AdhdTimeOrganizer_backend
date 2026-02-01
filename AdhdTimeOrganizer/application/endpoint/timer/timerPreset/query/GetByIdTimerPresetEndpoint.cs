using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.timer;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.timer.timerPreset.query;

public class GetByIdTimerPresetEndpoint(
    AppCommandDbContext dbContext,
    TimerPresetMapper mapper)
    : BaseGetByIdEndpoint<TimerPreset, TimerPresetResponse, TimerPresetMapper>(dbContext, mapper)
{
    protected override IQueryable<TimerPreset> WithIncludes(IQueryable<TimerPreset> query)
    {
        return query.Include(t => t.Activity);
    }
}
