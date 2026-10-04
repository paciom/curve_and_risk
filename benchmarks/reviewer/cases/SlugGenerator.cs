using System.Globalization;
using System.Text;

namespace Shop.Common;

public static class SlugGenerator
{
    private const int MaxLength = 80;

    /// <summary>Lower-case ASCII letters and digits separated by single hyphens; empty input gives an empty slug.</summary>
    public static string From(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var builder = new StringBuilder(title.Length);
        var pendingHyphen = false;

        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                var needsHyphen = pendingHyphen && builder.Length > 0;
                if (builder.Length + (needsHyphen ? 2 : 1) > MaxLength)
                {
                    break;
                }

                if (needsHyphen)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return builder.ToString();
    }
}
