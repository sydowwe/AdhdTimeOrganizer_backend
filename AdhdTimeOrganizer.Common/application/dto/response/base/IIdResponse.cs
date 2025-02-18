namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public interface IIdResponse : IMyResponse
{
    public long Id { get; init; }
}