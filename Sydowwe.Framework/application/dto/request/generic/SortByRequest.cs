using System.Diagnostics.CodeAnalysis;

namespace Sydowwe.Framework.application.dto.request.generic;

public record SortByRequest
{
    public required string Key { get; init; }
    public required bool IsDesc { get; init; }

    public SortByRequest()
    {
    }

    [SetsRequiredMembers]
    public SortByRequest(string key, bool isDesc)
    {
        Key = key;
        IsDesc = isDesc;
    }
}