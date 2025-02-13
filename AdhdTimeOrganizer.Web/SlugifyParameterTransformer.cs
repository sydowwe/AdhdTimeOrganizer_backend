using System.Text.RegularExpressions;

namespace AdhdTimeOrganizer.Web;

public partial class SlugifyParameterTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value)
    {
        if (value == null) return null;
        //admin controller
        value = MyRegex().Replace(value.ToString() ?? string.Empty,  "/admin/");
        return SlugifyRegex().Replace(value.ToString()!, "$1-$2").ToLower();
    }

    [GeneratedRegex("([a-z])([A-Z0-9])")]
    private static partial Regex SlugifyRegex();

    [GeneratedRegex("^Admin", RegexOptions.IgnoreCase)]
    private static partial Regex MyRegex();
}