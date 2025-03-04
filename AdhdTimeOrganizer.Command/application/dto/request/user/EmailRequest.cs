using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record EmailRequest : IMyRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    public EmailRequest()
    {
    }

    public EmailRequest(string email)
    {
        Email = email;
    }
}