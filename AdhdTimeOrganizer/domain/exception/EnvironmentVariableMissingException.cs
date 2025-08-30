namespace AdhdTimeOrganizer.domain.exception;

public class EnvironmentVariableMissingException(string name)
    : System.Exception($"Environment variable {name.ToUpper()} missing")
{
}