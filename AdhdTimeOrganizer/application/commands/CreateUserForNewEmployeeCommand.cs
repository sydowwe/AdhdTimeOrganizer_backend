using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.commands;

public record CreateUserForNewUserCommand : ICommand<Result<User>>
{
    public required string Username { get; init; }
    public required long JobTitleId { get; init; }
    public required bool AddMicrosoftAccount { get; init; }
    public string? MicrosoftAccountId { get; init; }
}