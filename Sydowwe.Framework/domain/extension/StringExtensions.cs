using System.Globalization;
using System.Text;

namespace Sydowwe.Framework.domain.extension;

public static class StringExtensions
{
    extension(string text)
    {
        public string RemoveDiacritics()
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in from c in normalizedString let unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c) where unicodeCategory != UnicodeCategory.NonSpacingMark select c)
                stringBuilder.Append(c);

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        public string RemoveNumbers()
        {
            return string.IsNullOrEmpty(text) ? text : new string(text.Where(c => !char.IsDigit(c)).ToArray());
        }
    }
}