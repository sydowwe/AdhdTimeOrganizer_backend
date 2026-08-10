using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entityInterface;

public interface IEntityWithIsDone : IEntityWithId
{
    public bool IsDone { get; set; }
}