namespace Sydowwe.Framework.domain.exception;

public class EnvironmentVariableMissingException(string name)
    : Exception($"Environment variable {name.ToUpper()} missing")
{
}