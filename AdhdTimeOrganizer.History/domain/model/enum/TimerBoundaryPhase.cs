namespace AdhdTimeOrganizer.History.domain.model.@enum;

/// <summary>
/// Which boundary of a running timer was crossed — what a <see cref="entity.timerAlarm.TimerBoundaryAlarm"/> rings
/// for, and what the boundary notification's title says. Lived in the notification contract while the renderer
/// that read it was the framework's; it moved here with the kind (R1). Appended, never reordered: the value is
/// persisted into the payload document and the alarm row by name.
/// </summary>
public enum TimerBoundaryPhase
{
    /// <summary>A plain countdown with no phase structure finished.</summary>
    Plain,

    /// <summary>A focus period ended — the user should take a break.</summary>
    Focus,

    /// <summary>A short break ended — back to work.</summary>
    ShortBreak,

    /// <summary>A long break ended — back to work.</summary>
    LongBreak
}
