using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UserRequest : IUpdateRequest
{
    public required string Email { get; set; }
    public required string Name { get; set; }
    public required string Surname { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MicrosoftAccountId { get; set; }
}