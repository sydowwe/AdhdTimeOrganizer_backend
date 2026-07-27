namespace Sydowwe.Framework.domain.entityInterface;

public interface IRequiresPasswordChange
{
    bool MustChangePassword { get; set; }
}