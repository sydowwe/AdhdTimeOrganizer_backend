using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ConfirmEmailChangeRequest : IMyRequest
{
    public string UserId { get; set; } = string.Empty;
    public string NewEmail { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}