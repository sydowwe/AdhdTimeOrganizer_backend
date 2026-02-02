using AdhdTimeOrganizer.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.command;

public class ActivityHeartbeatValidator : Validator<ActivityHeartbeatRequest>
{
    public ActivityHeartbeatValidator()
    {
        RuleFor(x => x.HeartbeatAt).NotEmpty();
        RuleFor(x => x.Events).NotNull();
        RuleForEach(x => x.Events).ChildRules(e =>
        {
            e.RuleFor(x => x.Domain).NotEmpty();
            e.RuleFor(x => x.At).NotEmpty();
        });
    }
}
