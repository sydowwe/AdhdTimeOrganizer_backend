using AdhdTimeOrganizer.Command.application.dto.request.user;
using AdhdTimeOrganizer.Command.application.dto.response.user;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AutoMapper;
using Google.Cloud.RecaptchaEnterprise.V1;

namespace AdhdTimeOrganizer.Command.application.mapper;

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<UserRequest, UserEntity>()
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(a => a.Email))
            .ForMember(dest => dest.Timezone, opt => opt.MapFrom(a => TimeZoneInfo.FindSystemTimeZoneById(a.Timezone)));
        CreateMap<RegistrationRequest, UserEntity>().ForMember(dest => dest.UserName, opt => opt.MapFrom(a => a.Email))
            .ForMember(dest => dest.Timezone, opt => opt.MapFrom(a => TimeZoneInfo.FindSystemTimeZoneById(a.Timezone)));

        CreateMap<UserEntity, TwoFactorAuthResponse>();
        CreateMap<UserEntity, LoginResponse>();
        CreateMap<UserEntity, UserResponse>();
        CreateMap<UserEntity, EditedUserResponse>();
    }
}