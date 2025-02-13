namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public interface IIdResponse : IMyResponse
{
    public long Id { get; }
}

public class IdResponse : IIdResponse
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