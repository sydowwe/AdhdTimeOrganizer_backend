using System.Text.RegularExpressions;

namespace Sydowwe.Framework.domain.helper;

/// <summary>
/// Masks personal data (PII) in free-text log output so emails, IBANs and Slovak birth numbers
/// never reach application logs in plaintext (GDPR Art. 5 storage limitation + Art. 32). The
/// <see cref="Scrub"/> method is meant to be applied at the logging sink as a defense-in-depth
/// catch-all — including over exception messages, which can embed row data because the DB
/// connection string runs with <c>Include Error Detail=true</c>.
/// </summary>
public static partial class PiiRedactor
{
    // Order matters: emails are matched before IBAN/birth-number so their digits aren't partially eaten.
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    // SK-style and generic IBAN: country code + 2 check digits + up to 30 alphanumerics, optionally spaced in groups.
    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Za-z0-9]){11,30}\b", RegexOptions.Compiled)]
    private static partial Regex IbanRegex();

    // Slovak birth number (rodné číslo): 6 digits + optional slash + 3-4 digits.
    [GeneratedRegex(@"\b\d{6}\s?/?\s?\d{3,4}\b", RegexOptions.Compiled)]
    private static partial Regex BirthNumberRegex();

    private const string EmailPlaceholder = "[email]";
    private const string IbanPlaceholder = "[iban]";
    private const string BirthNumberPlaceholder = "[birth-no]";

    /// <summary>
    /// Removes emails, IBANs and birth numbers from arbitrary text, replacing each with a tag.
    /// Safe to call on null/empty input.
    /// </summary>
    public static string? Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = EmailRegex().Replace(text, EmailPlaceholder);
        text = IbanRegex().Replace(text, IbanPlaceholder);
        text = BirthNumberRegex().Replace(text, BirthNumberPlaceholder);
        return text;
    }

    /// <summary>
    /// Partially masks an email for diagnostic log lines that legitimately need a stable hint of which
    /// address was involved without storing it in full, e.g. <c>j***@example.com</c>.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[email]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "***";

        var firstChar = email[0];
        var domain = email[atIndex..];
        return $"{firstChar}***{domain}";
    }
}