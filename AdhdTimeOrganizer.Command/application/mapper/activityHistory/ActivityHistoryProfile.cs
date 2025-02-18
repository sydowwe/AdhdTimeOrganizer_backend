using AdhdTimeOrganizer.Command.application.dto.request.history;
using AdhdTimeOrganizer.Command.application.dto.response.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activityHistory;

public class ActivityHistoryProfile : Profile
{
    public ActivityHistoryProfile()
    {
        CreateMap<ActivityHistoryRequest, ActivityHistory>().ForMember(dest => dest.EndTimestamp,
            opt => opt.MapFrom(src => src.StartTimestamp.AddSeconds(src.Length.GetInSeconds())));
        CreateMap<ActivityHistory, ActivityHistoryResponse>();
    }
}