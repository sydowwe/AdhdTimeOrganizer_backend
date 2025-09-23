namespace AdhdTimeOrganizer.infrastructure.settings;

public record TodoListSettings
{
    public int MaxItems { get; init; }
    public long DisplayOrderGap { get; init; }

    public long DisplayOrderStart => MaxItems * DisplayOrderGap;
}