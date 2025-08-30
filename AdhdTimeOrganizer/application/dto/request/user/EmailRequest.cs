using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record EmailRequest : IMyRequest
{
    public string Email { get; set; }
}