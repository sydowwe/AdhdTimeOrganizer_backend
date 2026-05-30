using AdhdTimeOrganizer.application.dto.request.@interface;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ConfirmEmailRequest
{
    public required long UserId { get; init; }
    public required string Token { get; init; }
}

public class ConfirmEmailValidator : Validator<ConfirmEmailRequest>
{
    public ConfirmEmailValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(1000);
    }
}
