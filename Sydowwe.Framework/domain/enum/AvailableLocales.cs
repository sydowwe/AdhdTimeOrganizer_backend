using System.Text.Json.Serialization;

namespace Sydowwe.Framework.domain.@enum;

public enum AvailableLocales
{
    [JsonStringEnumMemberName("SK")]
    Sk,

    [JsonStringEnumMemberName("EN")]
    En,

    [JsonStringEnumMemberName("CZ")]
    Cz
}