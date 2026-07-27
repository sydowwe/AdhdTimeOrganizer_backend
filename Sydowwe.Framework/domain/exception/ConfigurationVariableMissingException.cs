namespace Sydowwe.Framework.domain.exception;

public class ConfigurationVariableMissingException(string name)
    : Exception($"Configuration variable {name.ToUpper()} missing")
{
}