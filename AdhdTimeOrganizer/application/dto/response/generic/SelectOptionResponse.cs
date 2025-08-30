using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.generic;

public record SelectOptionResponse : IIdResponse
{
    public long Id { get; init; }
    public string Text { get; init; }

    public SelectOptionResponse()
    {
    }

    public SelectOptionResponse(long id, string text)
    {
        Id = id;
        Text = text;
    }
}