using System.Text.Json.Serialization;

namespace Sydowwe.Framework.domain.@enum;

public enum AppThemeEnum
{
    [JsonStringEnumMemberName("light")]
    Light,

    [JsonStringEnumMemberName("dark")]
    Dark,

    [JsonStringEnumMemberName("system")]
    System
}