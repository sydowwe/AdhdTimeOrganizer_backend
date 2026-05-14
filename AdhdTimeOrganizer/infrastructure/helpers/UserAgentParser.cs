namespace AdhdTimeOrganizer.infrastructure.helpers;

public static class UserAgentParser
{
    public static (string Device, string Browser) Parse(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return ("Unknown", "Unknown");

        return (ParseDevice(userAgent), ParseBrowser(userAgent));
    }

    private static string ParseDevice(string ua)
    {
        if (ua.Contains("Android")) return "Android";
        if (ua.Contains("iPhone") || ua.Contains("iPad")) return "iOS";
        if (ua.Contains("Windows")) return "Windows";
        if (ua.Contains("Mac OS X")) return "macOS";
        if (ua.Contains("Linux")) return "Linux";
        return "Unknown";
    }

    private static string ParseBrowser(string ua)
    {
        if (ua.Contains("Edg/")) return $"Edge {MajorVersion(ua, "Edg/")}";
        if (ua.Contains("OPR/")) return $"Opera {MajorVersion(ua, "OPR/")}";
        if (ua.Contains("Chrome/")) return $"Chrome {MajorVersion(ua, "Chrome/")}";
        if (ua.Contains("Firefox/")) return $"Firefox {MajorVersion(ua, "Firefox/")}";
        if (ua.Contains("Safari/") && ua.Contains("Version/")) return $"Safari {MajorVersion(ua, "Version/")}";
        return "Unknown";
    }

    private static string MajorVersion(string ua, string prefix)
    {
        var idx = ua.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return "";
        var start = idx + prefix.Length;
        var end = ua.IndexOfAny([' ', '.'], start);
        return end < 0 ? ua[start..] : ua[start..end];
    }
}
