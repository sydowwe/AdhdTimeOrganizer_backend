using Sydowwe.Framework.application.dto.response.@base;

namespace Sydowwe.Framework.application.dto.response.user;

public record EmailResponse : IMyResponse
{
    public required string Email { get; init; }
}