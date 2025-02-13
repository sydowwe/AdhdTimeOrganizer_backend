using AdhdTimeOrganizer.Common.domain.model.entity;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.@base;

public interface IStartAndLengthRepository<TEntity>
    where TEntity : BaseEntity
{
    Task<IEnumerable<TEntity>> GetAllByDateAndHourSpan(int userId, DateTime startDate, DateTime endDate);
}