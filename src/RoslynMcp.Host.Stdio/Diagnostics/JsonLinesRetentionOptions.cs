using System.Globalization;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// Per-process retention policy for the JSON-lines file sink: the rotation threshold for one
/// segment and how many rotated (numbered) segments are kept beside the active file.
/// </summary>
internal sealed class JsonLinesRetentionOptions
{
    public const string MaxSegmentBytesVariable = "ROSLYNMCP_OBSERVABILITY_MAX_SEGMENT_BYTES";
    public const string MaxSegmentsVariable = "ROSLYNMCP_OBSERVABILITY_MAX_SEGMENTS";
    public const long DefaultMaxSegmentBytes = 5L * 1024 * 1024;
    public const int DefaultMaxSegments = 3;

    /// <summary>
    /// Smallest accepted threshold. Far above the largest bounded truncation marker so a marker always
    /// fits in an empty segment.
    /// </summary>
    public const long MinimumSegmentBytes = 8 * 1024;

    public const long MaximumSegmentBytes = 1024L * 1024 * 1024;
    public const int MaximumSegments = 100;

    private JsonLinesRetentionOptions(long maxSegmentBytes, int maxSegments)
    {
        MaxSegmentBytes = maxSegmentBytes;
        MaxSegments = maxSegments;
    }

    public static JsonLinesRetentionOptions Default { get; } =
        new(DefaultMaxSegmentBytes, DefaultMaxSegments);

    /// <summary>Rotation threshold in UTF-8 bytes for one segment file.</summary>
    public long MaxSegmentBytes { get; }

    /// <summary>Rotated segments (<c>.1</c> newest to <c>.N</c> oldest) kept beside the active file.</summary>
    public int MaxSegments { get; }

    public static JsonLinesRetentionOptions Create(long maxSegmentBytes, int maxSegments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegmentBytes, MinimumSegmentBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSegmentBytes, MaximumSegmentBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSegments, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSegments, MaximumSegments);
        return new(maxSegmentBytes, maxSegments);
    }

    public static JsonLinesRetentionOptions Parse(Func<string, string?> readValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        return Create(
            ReadInteger(readValue, MaxSegmentBytesVariable, DefaultMaxSegmentBytes, MinimumSegmentBytes, MaximumSegmentBytes),
            (int)ReadInteger(readValue, MaxSegmentsVariable, DefaultMaxSegments, 1, MaximumSegments));
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
