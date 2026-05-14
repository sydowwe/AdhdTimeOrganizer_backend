using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.domain.model.@enum;

public enum AvailableLocales
{
    [JsonStringEnumMemberName("SK")]
    Sk,
    [JsonStringEnumMemberName("EN")]
    En,
    [JsonStringEnumMemberName("CZ")]
    Cz
}