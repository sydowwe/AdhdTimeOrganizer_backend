using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class EmailResponse : IMyResponse
{
    public string Email { get; set; }
}