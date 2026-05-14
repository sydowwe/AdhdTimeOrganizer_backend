using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.domain.model.@enum;

public enum AppThemeEnum
{
    [JsonStringEnumMemberName("light")]
    Light,
    [JsonStringEnumMemberName("dark")]
    Dark,
    [JsonStringEnumMemberName("system")]
    System
}
