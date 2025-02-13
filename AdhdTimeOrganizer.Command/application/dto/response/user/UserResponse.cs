using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class UserResponse : IdResponse
{
    public string Email { get; set; }
    public bool TwoFactorEnabled { get; set; }
}