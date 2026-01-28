using AdhdTimeOrganizer.application.dto.request.timer;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.timer;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.timer;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper.timer;

[Mapper]
public partial class TimerPresetMapper : IBaseSimpleCrudMapper<TimerPreset, TimerPresetRequest, TimerPresetResponse>
{
    public partial TimerPresetResponse ToResponse(TimerPreset entity);

    public partial TimerPreset ToEntity(TimerPresetRequest request, long userId);

    public partial void UpdateEntity(TimerPresetRequest request, TimerPreset entity);

    public partial IQueryable<TimerPresetResponse> ProjectToResponse(IQueryable<TimerPreset> query);
    public partial SelectOptionResponse ToSelectOptionResponse(TimerPreset entity);
}
