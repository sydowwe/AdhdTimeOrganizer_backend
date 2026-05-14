using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class UserMapper : IBaseCreateWithoutUserMapper<User, UserRequest>, IBaseUpdateMapper<User, UserRequest>, IBaseResponseMapper<User, UserResponse>
{
    public partial UserResponse ToResponse(User entity);

    [MapProperty(nameof(User.CreatedTimestamp), nameof(UserDataResponse.CreatedAt))]
    [MapProperty(nameof(User.CurrentLocale), nameof(UserDataResponse.Locale))]
    [MapProperty(nameof(User.Timezone), nameof(UserDataResponse.Timezone), Use = nameof(TimezoneToString))]
    public partial UserDataResponse ToDataResponse(User entity);

    [MapProperty(nameof(UserRequest.Timezone), nameof(User.Timezone), Use = nameof(ToTimeZoneInfo))]
    public partial User ToEntity(UserRequest request);

    [MapProperty(nameof(UserRequest.Timezone), nameof(User.Timezone), Use = nameof(ToTimeZoneInfo))]
    public partial void UpdateEntity(UserRequest request, User entity);

    public partial IQueryable<UserResponse> ProjectToResponse(IQueryable<User> query);

    private static TimeZoneInfo ToTimeZoneInfo(string timeZoneId)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    private static string TimezoneToString(TimeZoneInfo tz) => tz.Id;

    public User ToEntityWithGoogleId(GoogleAuthRegistrationRequest request)
    {
        var user = ToEntity(request);
        user.GoogleOAuthUserId = request.GoogleOAuthUserId;
        return user;
    }
}
