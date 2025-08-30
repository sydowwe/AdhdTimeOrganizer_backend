using AdhdTimeOrganizer.domain.result;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.commands;

public class GetAccessTokenCommand : ICommand<Result<string>>
{
    public string[] Scopes { get; set; } = [];
}