using System.Globalization;
using System.Text;

namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// The single authority for a process-owned segment set: the active file
/// <c>roslyn-mcp-{pid}.jsonl</c> plus numbered rotated segments <c>roslyn-mcp-{pid}.{n}.jsonl</c>
/// (<c>.1</c> newest). Rotation shifts every segment up one slot; only the oldest is evicted, by replacement after its successor moved.
/// Not thread-safe: the owner serializes calls. Later sinks and sweeps reuse this seam instead of
/// re-implementing rotation.
/// </summary>
internal sealed class JsonLinesSegmentWriter
{
    internal static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;
    private readonly string _stem;
    private readonly JsonLinesRetentionOptions _options;

    internal JsonLinesSegmentWriter(string directory, int processId, JsonLinesRetentionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(options);
        _directory = Path.GetFullPath(directory);
        _stem = "roslyn-mcp-" + processId.ToString(CultureInfo.InvariantCulture);
        _options = options;
        ActivePath = Path.Combine(_directory, _stem + ".jsonl");
    }

    internal string ActivePath { get; }

    internal long MaxSegmentBytes => _options.MaxSegmentBytes;

    /// <summary>Path of rotated segment <paramref name="index"/> (1 = newest).</summary>
    internal string SegmentPath(int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return Path.Combine(_directory, _stem + "." + index.ToString(CultureInfo.InvariantCulture) + ".jsonl");
    }

    /// <summary>Length of the active file in bytes; zero when it does not exist.</summary>
    internal long CurrentLength() => File.Exists(ActivePath) ? new FileInfo(ActivePath).Length : 0;

    /// <summary>Rotates when appending <paramref name="pendingBytes"/> would pass the threshold, then appends.</summary>
    internal void Append(string line, int pendingBytes)
    {
        Directory.CreateDirectory(_directory);
        RotateIfNeeded(pendingBytes);
        File.AppendAllText(ActivePath, line, Utf8WithoutBom);
    }

    internal void RotateIfNeeded(int pendingBytes)
    {
        var currentLength = CurrentLength();
        if (currentLength == 0 || currentLength + pendingBytes <= _options.MaxSegmentBytes)
        {
            return;
        }

        Rotate();
    }

    /// <summary>
    /// Moves the active file to <c>.1</c> and shifts existing segments up one slot, closing gaps. Failure-atomic:
    /// nothing is deleted. Segments move oldest first with replace semantics, so the one segment that
    /// falls off the end (slot N) is overwritten only by a Move that succeeded. A Move that throws leaves
    /// every segment on disk; the next rotation recompacts from what exists.
    /// </summary>
    internal void Rotate()
    {
        var existing = new List<int>();
        for (var index = 1; index <= _options.MaxSegments; index++)
        {
            if (File.Exists(SegmentPath(index)))
            {
                existing.Add(index);
            }
        }

        // existing[position - 1] is the position-th newest segment; it belongs in slot position + 1.
        for (var position = existing.Count; position >= 1; position--)
        {
            var target = position + 1;
            var source = existing[position - 1];
            if (target > _options.MaxSegments || source == target)
            {
                continue;
            }

            File.Move(SegmentPath(source), SegmentPath(target), overwrite: true);
        }

        File.Move(ActivePath, SegmentPath(1), overwrite: true);
    }
}
