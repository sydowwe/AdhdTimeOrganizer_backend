using Sydowwe.Framework.application.dto.dto;

namespace Sydowwe.Framework.domain.extension;

public static class TimeOnlyExtensions
{
    extension(TimeOnly time)
    {
        public TimeDto ToDto => new(time.Hour, time.Minute);
    }
}