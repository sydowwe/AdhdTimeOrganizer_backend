using AdhdTimeOrganizer.application.dto.request.user;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UserPlannerSettingsValidator : Validator<UserPlannerSettingsRequest>
{
    public UserPlannerSettingsValidator()
    {
        RuleFor(x => x.ReminderMinutesBefore).InclusiveBetween(0, 120);
        RuleFor(x => x.SlotDurationMinutes).InclusiveBetween(1, 120);
        RuleFor(x => x.DefaultConflictResolution).IsInEnum();
        RuleFor(x => x.DefaultApplyTemplateId).GreaterThan(0).When(x => x.DefaultApplyTemplateId.HasValue);
        RuleFor(x => x.PredefinedSkipReasons).NotNull();
        RuleForEach(x => x.PredefinedSkipReasons).MaximumLength(100);
    }
}
