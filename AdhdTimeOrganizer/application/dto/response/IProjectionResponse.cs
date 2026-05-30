namespace AdhdTimeOrganizer.application.dto.response;

public interface IProjectionResponse<out TResponse, in TEntity>
    where TResponse : class
    where TEntity : class
{
    static abstract IQueryable<TResponse> Projection(IQueryable<TEntity> query);
    static abstract TResponse FromEntity(TEntity entity);
}