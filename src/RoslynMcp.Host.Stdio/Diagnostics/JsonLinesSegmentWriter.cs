using System.Globalization;
using System.Text;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// The single authority for a process-owned segment set: the active file
/// <c>roslyn-mcp-{pid}.jsonl</c> plus numbered rotated segments <c>roslyn-mcp-{pid}.{n}.jsonl</c>
/// (<c>.1</c> newest). Rotation shifts every segment up one slot and deletes only the oldest.
/// Not thread-safe: the owner serializes calls. Later sinks and sweeps reuse this seam instead of
/// re-implementing rotation.
/// </summary>
internal sealed class JsonLinesSegmentWriter
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _stem;
    private readonly JsonLinesRetentionOptions _options;

    public JsonLinesSegmentWriter(string directory, int processId, JsonLinesRetentionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(options);
        _directory = Path.GetFullPath(directory);
        _stem = "roslyn-mcp-" + processId.ToString(CultureInfo.InvariantCulture);
        _options = options;
        ActivePath = Path.Combine(_directory, _stem + ".jsonl");
    }

    public string ActivePath { get; }

    public long MaxSegmentBytes => _options.MaxSegmentBytes;

    /// <summary>Path of rotated segment <paramref name="index"/> (1 = newest).</summary>
    public string SegmentPath(int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return Path.Combine(_directory, _stem + "." + index.ToString(CultureInfo.InvariantCulture) + ".jsonl");
    }

    /// <summary>Length of the active file in bytes; zero when it does not exist.</summary>
    public long CurrentLength() => File.Exists(ActivePath) ? new FileInfo(ActivePath).Length : 0;

    /// <summary>Rotates when appending <paramref name="pendingBytes"/> would pass the threshold, then appends.</summary>
    public void Append(string line, int pendingBytes)
    {
        Directory.CreateDirectory(_directory);
        RotateIfNeeded(pendingBytes);
        File.AppendAllText(ActivePath, line, _utf8WithoutBom);
    }

    public void RotateIfNeeded(int pendingBytes)
    {
        var currentLength = CurrentLength();
        if (currentLength == 0 || currentLength + pendingBytes <= _options.MaxSegmentBytes)
        {
            return;
        }

        Rotate();
    }

    /// <summary>Deletes the oldest segment, shifts the rest up one slot, and moves the active file to <c>.1</c>.</summary>
    public void Rotate()
    {
        var oldest = SegmentPath(_options.MaxSegments);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = _options.MaxSegments - 1; index >= 1; index--)
        {
            var source = SegmentPath(index);
            if (File.Exists(source))
            {
                File.Move(source, SegmentPath(index + 1));
            }
        }

        File.Move(ActivePath, SegmentPath(1));
    }
}
