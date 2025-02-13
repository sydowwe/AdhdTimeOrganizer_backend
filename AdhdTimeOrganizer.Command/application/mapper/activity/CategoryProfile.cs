using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.application.dto.response.@base;
using AdhdTimeOrganizer.Common.application.dto.response.generic;
using AutoMapper;

namespace AdhdTimeOrganizer.Command.application.mapper.activity;

public class CategoryProfile : Profile
{
    public CategoryProfile()
    {
        CreateMap<NameTextColorIconRequest, Category>();
        CreateMap<Category, NameTextColorIconResponse>();
        CreateMap<Category, SelectOptionResponse>().ForMember(dest => dest.Label, opt => opt.MapFrom(src => src.Name));
    }
}