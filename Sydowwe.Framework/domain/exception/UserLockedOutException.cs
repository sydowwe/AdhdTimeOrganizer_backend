namespace Sydowwe.Framework.domain.exception;

public class UserLockedOutException(int lockOutTime) : Exception($"Locked out for {lockOutTime} minutes")
{
}