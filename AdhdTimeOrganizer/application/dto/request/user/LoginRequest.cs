using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record LoginRequest : IMyRequest
{
    public bool StayLoggedIn { get; set; }
    public string RecaptchaToken { get; set; }
    public string Timezone { get; set; }
}