using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.timer;


namespace AdhdTimeOrganizer.application.dto.response.timer;

public record PomodoroTimerPresetResponse : IdResponse, IProjectionResponse<PomodoroTimerPresetResponse, PomodoroTimerPreset>
{
    public required string Name { get; init; }
    public required int FocusDuration { get; init; }
    public required int ShortBreakDuration { get; init; }
    public required int LongBreakDuration { get; init; }
    public required int FocusPeriodInCycleCount { get; init; }
    public required int NumberOfCycles { get; init; }
    public ActivityResponse? FocusActivity { get; init; }
    public ActivityResponse? RestActivity { get; init; }

    public static IQueryable<PomodoroTimerPresetResponse> Projection(IQueryable<PomodoroTimerPreset> q) =>
        q.Select(e => new PomodoroTimerPresetResponse
        {
            Id = e.Id,
            Name = e.Name,
            FocusDuration = e.FocusDuration,
            ShortBreakDuration = e.ShortBreakDuration,
            LongBreakDuration = e.LongBreakDuration,
            FocusPeriodInCycleCount = e.FocusPeriodInCycleCount,
            NumberOfCycles = e.NumberOfCycles,
            FocusActivity = e.FocusActivity == null ? null : new ActivityResponse
            {
                Id = e.FocusActivity.Id,
                Name = e.FocusActivity.Name,
                Text = e.FocusActivity.Text,
                IsUnavoidable = e.FocusActivity.IsUnavoidable,
                Role = new ActivityRoleResponse
                {
                    Id = e.FocusActivity.Role.Id,
                    Name = e.FocusActivity.Role.Name,
                    Text = e.FocusActivity.Role.Text,
                    Color = e.FocusActivity.Role.Color,
                    Icon = e.FocusActivity.Role.Icon,
                },
                Category = e.FocusActivity.Category == null ? null : new ActivityCategoryResponse
                {
                    Id = e.FocusActivity.Category.Id,
                    Name = e.FocusActivity.Category.Name,
                    Text = e.FocusActivity.Category.Text,
                    Color = e.FocusActivity.Category.Color,
                    Icon = e.FocusActivity.Category.Icon,
                },
            },
            RestActivity = e.RestActivity == null ? null : new ActivityResponse
            {
                Id = e.RestActivity.Id,
                Name = e.RestActivity.Name,
                Text = e.RestActivity.Text,
                IsUnavoidable = e.RestActivity.IsUnavoidable,
                Role = new ActivityRoleResponse
                {
                    Id = e.RestActivity.Role.Id,
                    Name = e.RestActivity.Role.Name,
                    Text = e.RestActivity.Role.Text,
                    Color = e.RestActivity.Role.Color,
                    Icon = e.RestActivity.Role.Icon,
                },
                Category = e.RestActivity.Category == null ? null : new ActivityCategoryResponse
                {
                    Id = e.RestActivity.Category.Id,
                    Name = e.RestActivity.Category.Name,
                    Text = e.RestActivity.Category.Text,
                    Color = e.RestActivity.Category.Color,
                    Icon = e.RestActivity.Category.Icon,
                },
            },
        });

    public static PomodoroTimerPresetResponse FromEntity(PomodoroTimerPreset e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        FocusDuration = e.FocusDuration,
        ShortBreakDuration = e.ShortBreakDuration,
        LongBreakDuration = e.LongBreakDuration,
        FocusPeriodInCycleCount = e.FocusPeriodInCycleCount,
        NumberOfCycles = e.NumberOfCycles,
        FocusActivity = e.FocusActivity == null ? null : new ActivityResponse
        {
            Id = e.FocusActivity.Id,
            Name = e.FocusActivity.Name,
            Text = e.FocusActivity.Text,
            IsUnavoidable = e.FocusActivity.IsUnavoidable,
            Role = new ActivityRoleResponse
            {
                Id = e.FocusActivity.Role.Id,
                Name = e.FocusActivity.Role.Name,
                Text = e.FocusActivity.Role.Text,
                Color = e.FocusActivity.Role.Color,
                Icon = e.FocusActivity.Role.Icon,
            },
            Category = e.FocusActivity.Category == null ? null : new ActivityCategoryResponse
            {
                Id = e.FocusActivity.Category.Id,
                Name = e.FocusActivity.Category.Name,
                Text = e.FocusActivity.Category.Text,
                Color = e.FocusActivity.Category.Color,
                Icon = e.FocusActivity.Category.Icon,
            },
        },
        RestActivity = e.RestActivity == null ? null : new ActivityResponse
        {
            Id = e.RestActivity.Id,
            Name = e.RestActivity.Name,
            Text = e.RestActivity.Text,
            IsUnavoidable = e.RestActivity.IsUnavoidable,
            Role = new ActivityRoleResponse
            {
                Id = e.RestActivity.Role.Id,
                Name = e.RestActivity.Role.Name,
                Text = e.RestActivity.Role.Text,
                Color = e.RestActivity.Role.Color,
                Icon = e.RestActivity.Role.Icon,
            },
            Category = e.RestActivity.Category == null ? null : new ActivityCategoryResponse
            {
                Id = e.RestActivity.Category.Id,
                Name = e.RestActivity.Category.Name,
                Text = e.RestActivity.Category.Text,
                Color = e.RestActivity.Category.Color,
                Icon = e.RestActivity.Category.Icon,
            },
        },
    };
}
