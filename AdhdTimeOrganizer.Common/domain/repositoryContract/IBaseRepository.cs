using AdhdTimeOrganizer.Common.domain.model.entity;

namespace AdhdTimeOrganizer.Common.domain.repositoryContract;

public interface IBaseRepository<T>
    where T : BaseEntity
{
    public IQueryable<T> GetAsQueryable();
}