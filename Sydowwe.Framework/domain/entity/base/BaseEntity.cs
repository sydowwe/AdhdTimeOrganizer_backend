using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.@base;

public class BaseEntity : IEntityWithId
{
    public long Id { get; set; }
}