namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Shared CSV cell escaping for the module-local export services. Quotes cells that contain the
/// (semicolon) delimiter, quotes or newlines, and neutralizes spreadsheet formula injection: a cell
/// whose value starts with <c>= + - @</c> (optionally after whitespace) would otherwise be executed
/// as a formula when the file is opened in Excel/Sheets, so we prefix a leading apostrophe.
/// </summary>
public static class CsvHelper
{
    private static readonly char[] DangerousLeadingChars = ['=', '+', '-', '@'];

    public static string EscapeCsv(string field)
    {
        field = NeutralizeFormula(field);

        if (field.IndexOfAny([';', '"', '\n', '\r']) < 0)
            return field;
        return $"\"{field.Replace("\"", "\"\"")}\"";
    }

    private static string NeutralizeFormula(string field)
    {
        var firstNonWhitespace = field.AsSpan().TrimStart();
        if (firstNonWhitespace.Length > 0 && DangerousLeadingChars.Contains(firstNonWhitespace[0]))
            return "'" + field;
        return field;
    }
}