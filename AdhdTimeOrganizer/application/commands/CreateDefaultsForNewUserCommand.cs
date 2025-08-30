using AdhdTimeOrganizer.domain.result;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.commands;

public record CreateDefaultsForNewUserCommand : ICommand<Result>
{
    public required long UserId { get; init; }
}