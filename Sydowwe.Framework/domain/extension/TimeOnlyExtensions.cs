using AdhdTimeOrganizer.application.dto.dto;

namespace AdhdTimeOrganizer.domain.extension;

public static class TimeOnlyExtensions
{
    extension(TimeOnly time)
    {
        public TimeDto ToDto => new(time.Hour, time.Minute);
    }
}