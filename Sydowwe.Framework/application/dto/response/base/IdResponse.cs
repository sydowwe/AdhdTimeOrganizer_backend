namespace Sydowwe.Framework.application.dto.response.@base;

public record IdResponse : IIdResponse
{
    public required long Id { get; init; }

    public IdResponse()
    {
    }

    public IdResponse(long id)
    {
        Id = id;
    }
}