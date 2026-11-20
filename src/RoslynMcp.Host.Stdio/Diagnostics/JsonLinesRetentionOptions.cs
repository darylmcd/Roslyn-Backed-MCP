using System.Globalization;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Per-process retention policy for the JSON-lines file sink: the rotation threshold for one
/// segment, how many rotated (numbered) segments are kept beside the active file, and the startup
/// sweep policy for files left by other (dead) processes: a maximum age and a total byte cap.
/// </summary>
internal sealed class JsonLinesRetentionOptions
{
    public const string MaxSegmentBytesVariable = "ROSLYNMCP_OBSERVABILITY_MAX_SEGMENT_BYTES";
    public const string MaxSegmentsVariable = "ROSLYNMCP_OBSERVABILITY_MAX_SEGMENTS";
    public const string RetentionDaysVariable = "ROSLYNMCP_OBSERVABILITY_RETENTION_DAYS";
    public const string MaxTotalBytesVariable = "ROSLYNMCP_OBSERVABILITY_MAX_TOTAL_BYTES";
    public const int DefaultRetentionDays = 14;
    public const long DefaultMaxTotalBytes = 64L * 1024 * 1024;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 3650;
    public const long MinimumMaxTotalBytes = 1024L * 1024;
    public const long MaximumMaxTotalBytes = 1024L * 1024 * 1024 * 1024;
    public const long DefaultMaxSegmentBytes = 5L * 1024 * 1024;
    public const int DefaultMaxSegments = 3;

    /// <summary>
    /// Smallest accepted threshold. Far above the largest bounded truncation marker so a marker always
    /// fits in an empty segment.
    /// </summary>
    public const long MinimumSegmentBytes = 8 * 1024;

    public const long MaximumSegmentBytes = 1024L * 1024 * 1024;
    public const int MaximumSegments = 100;

    private JsonLinesRetentionOptions(long maxSegmentBytes, int maxSegments, int retentionDays, long maxTotalBytes)
    {
        MaxSegmentBytes = maxSegmentBytes;
        MaxSegments = maxSegments;
        RetentionDays = retentionDays;
        MaxTotalBytes = maxTotalBytes;
    }

    public static JsonLinesRetentionOptions Default { get; } =
        new(DefaultMaxSegmentBytes, DefaultMaxSegments, DefaultRetentionDays, DefaultMaxTotalBytes);

    /// <summary>Rotation threshold in UTF-8 bytes for one segment file.</summary>
    public long MaxSegmentBytes { get; }

    /// <summary>Rotated segments (<c>.1</c> newest to <c>.N</c> oldest) kept beside the active file.</summary>
    public int MaxSegments { get; }

    /// <summary>Files of other processes last written longer ago than this many days are swept at startup.</summary>
    public int RetentionDays { get; }

    /// <summary>Total bytes of all sink files in the log directory the startup sweep trims toward, oldest first.</summary>
    public long MaxTotalBytes { get; }

    public static JsonLinesRetentionOptions Create(long maxSegmentBytes, int maxSegments, int retentionDays, long maxTotalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegmentBytes, MinimumSegmentBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSegmentBytes, MaximumSegmentBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSegments, MaximumSegments);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, MinimumRetentionDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionDays, MaximumRetentionDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTotalBytes, MinimumMaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxTotalBytes, MaximumMaxTotalBytes);
        return new(maxSegmentBytes, maxSegments, retentionDays, maxTotalBytes);
    }

    public static JsonLinesRetentionOptions Parse(Func<string, string?> readValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        return Create(
            ReadInteger(readValue, MaxSegmentBytesVariable, DefaultMaxSegmentBytes, MinimumSegmentBytes, MaximumSegmentBytes),
            (int)ReadInteger(readValue, MaxSegmentsVariable, DefaultMaxSegments, 1, MaximumSegments),
            (int)ReadInteger(readValue, RetentionDaysVariable, DefaultRetentionDays, MinimumRetentionDays, MaximumRetentionDays),
            ReadInteger(readValue, MaxTotalBytesVariable, DefaultMaxTotalBytes, MinimumMaxTotalBytes, MaximumMaxTotalBytes));
    }

    private static long ReadInteger(
        Func<string, string?> readValue,
        string variableName,
        long fallback,
        long minimum,
        long maximum)
    {
        var value = readValue(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            // The rejected value is never echoed: operators may paste secrets into the wrong variable.
            throw InvalidOperationErrors.Internal(
                $"{variableName} must be an integer between " +
                $"{minimum.ToString(CultureInfo.InvariantCulture)} and " +
                $"{maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }
}
