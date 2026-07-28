using System.Globalization;
using System.Text.RegularExpressions;

namespace Monitoring.Blazor.Services;

public static partial class IisLogFileNameParser
{
    public static bool TryGetLogDate(string fileName, out DateOnly logDate)
    {
        logDate = default;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var match = FileNamePattern().Match(fileName.Trim());
        if (!match.Success)
        {
            return false;
        }

        var yy = match.Groups["yy"].Value;
        var mm = match.Groups["mm"].Value;
        var dd = match.Groups["dd"].Value;
        return DateOnly.TryParseExact($"{yy}{mm}{dd}", "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out logDate);
    }

    [GeneratedRegex(@"^(?:u_)?(?:ex)(?<yy>\d{2})(?<mm>\d{2})(?<dd>\d{2})(?:[._-].*)?\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
