namespace AdhdTimeOrganizer.TodoLists.infrastructure.settings;

public record TodoListSettings
{
    public int MaxItems { get; init; }
    public long DisplayOrderGap { get; init; }

    /// <summary>
    /// How far ahead the dashboard widget looks, in whole days of the user's own calendar. Defaulted rather
    /// than required: the widget is a read-only convenience, so a host that omits the key gets the horizon
    /// the endpoint shipped with instead of an empty list.
    /// </summary>
    public int DashboardHorizonDays { get; init; } = 3;

    public long DisplayOrderStart => MaxItems * DisplayOrderGap;
}