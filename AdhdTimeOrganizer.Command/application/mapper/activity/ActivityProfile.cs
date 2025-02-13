using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activity;

public class ActivityProfile: Profile
{
    public ActivityProfile()
    {
        CreateMap<ActivityRequest, Activity>();
        CreateMap<Activity, ActivityResponse>();
        CreateMap<Activity, ActivityFormSelectOptionsResponse>()
            .ForMember(dest=>dest.Label,opt=>opt.MapFrom(a=>a.Name))
            .ForMember(dest=>dest.RoleOption, opt=>opt.MapFrom(a=>a.Role))
            .ForMember(dest=>dest.CategoryOption, opt=>opt.MapFrom(a=>a.Category));
    }
}