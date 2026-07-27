namespace Sydowwe.Framework.application.dto.response;

public interface IProjectionResponse<out TResponse, in TEntity>
    where TResponse : class
    where TEntity : class
{
    static abstract IQueryable<TResponse> Projection(IQueryable<TEntity> query);
}