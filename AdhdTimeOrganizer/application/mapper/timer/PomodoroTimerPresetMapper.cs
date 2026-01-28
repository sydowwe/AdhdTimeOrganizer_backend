using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.timer;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.timer;

[Mapper]
public partial class PomodoroTimerPresetMapper : IBaseSimpleCrudMapper<PomodoroTimerPreset, PomodoroTimerPresetRequest, PomodoroTimerPresetResponse>
{
    public partial PomodoroTimerPresetResponse ToResponse(PomodoroTimerPreset entity);

    public partial PomodoroTimerPreset ToEntity(PomodoroTimerPresetRequest request, long userId);

    public partial void UpdateEntity(PomodoroTimerPresetRequest request, PomodoroTimerPreset entity);

    public partial IQueryable<PomodoroTimerPresetResponse> ProjectToResponse(IQueryable<PomodoroTimerPreset> query);

    public partial SelectOptionResponse ToSelectOptionResponse(PomodoroTimerPreset entity);
}
