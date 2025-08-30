namespace AdhdTimeOrganizer.application.dto.response.@base;

public record IdResponse : IIdResponse
{
    public long Id { get; init; }

    public IdResponse()
    {
    }

    public IdResponse(long id)
    {
        Id = id;
    }
}