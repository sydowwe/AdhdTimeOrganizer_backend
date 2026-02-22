using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record SortByRequest
{
    [Required] public required string Key { get; init; }
    [Required] public required bool IsDesc { get; init; }

    public SortByRequest() { }

    [SetsRequiredMembers]
    public SortByRequest(string key, bool isDesc)
    {
        Key = key;
        IsDesc = isDesc;
    }
}