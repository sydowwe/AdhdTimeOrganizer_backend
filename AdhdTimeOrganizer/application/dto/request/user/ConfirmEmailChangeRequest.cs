using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ConfirmEmailChangeRequest
{
    public required long UserId { get; set; }
    public required string NewEmail { get; set; }
    public required string Token { get; set; }
}