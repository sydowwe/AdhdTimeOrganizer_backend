namespace AdhdTimeOrganizer.Common.domain.exception;

public class UserLockedOutException(int lockOutTime) : System.Exception($"Locked out for {lockOutTime} minutes")
{
}