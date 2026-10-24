using System.Globalization;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Generated storage namespace shared by placement, expiry and source watchers.</summary>
internal static class ForkStorageDirectoryName
{
    private const string Prefix = ".roslynmcp-fork-";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    internal static string Create(DateTimeOffset timestamp, string suffix) =>
        Prefix + timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture) + "-" + suffix;

    internal static bool IsGenerated(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) && name.Length > Prefix.Length + 20 &&
        name[Prefix.Length + 19] == '-' && TryGetCreated(name, out _);

    internal static bool TryGetCreated(string name, out DateTimeOffset created)
    {
        var stamped = name.StartsWith(Prefix, StringComparison.Ordinal) ? name[Prefix.Length..] : name;
        var dashIndex = stamped.IndexOf('-');
        var timestamp = dashIndex > 0 ? stamped[..dashIndex] : stamped;
        return DateTimeOffset.TryParseExact(timestamp, TimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out created);
    }
}
