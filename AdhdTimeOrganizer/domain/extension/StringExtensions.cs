﻿using System.Globalization;
using System.Text;

namespace AdhdTimeOrganizer.domain.extension;

public static class StringExtensions
{
    public static string RemoveDiacritics(this string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
    public static string RemoveNumbers(this string text)
    {
        return string.IsNullOrEmpty(text) ? text : new string(text.Where(c => !char.IsDigit(c)).ToArray());
    }
}