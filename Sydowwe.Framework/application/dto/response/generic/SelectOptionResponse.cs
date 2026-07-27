using System.Diagnostics.CodeAnalysis;
using Sydowwe.Framework.application.dto.response.@base;

namespace Sydowwe.Framework.application.dto.response.generic;

public record SelectOptionResponse : IIdResponse
{
    public required long Id { get; init; }
    public required string Text { get; init; }

    public SelectOptionResponse()
    {
    }

    [SetsRequiredMembers]
    public SelectOptionResponse(long id, string text)
    {
        Id = id;
        Text = text;
    }
}