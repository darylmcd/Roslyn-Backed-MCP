using System.Globalization;
using System.Text;
using System.Text.Json;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>Bounds successful reference pages by their complete serialized UTF-8 payload.</summary>
public sealed class ReferenceResponsePager
{
    public const string EnvironmentVariableName = "ROSLYNMCP_REFERENCE_RESPONSE_MAX_BYTES";
    public const int DefaultMaxBytes = 32_000;
    public const int MinimumMaxBytes = 1_024;
    internal static ReferenceResponsePager Default { get; } = new();

    private static readonly JsonSerializerOptions Compact = new(JsonDefaults.Indented) { WriteIndented = false };
    private readonly int _maxBytes;

    public ReferenceResponsePager(int maxBytes = DefaultMaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, MinimumMaxBytes);
        _maxBytes = maxBytes;
    }

    public static ReferenceResponsePager FromEnvironment(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxBytes)
            && maxBytes >= MinimumMaxBytes
            ? new(maxBytes)
            : Default;

    internal string Serialize(
        IReadOnlyList<LocationDto> results,
        int offset,
        int limit,
        bool summary,
        CancellationToken ct = default)
    {
        ParameterValidation.ValidatePagination(offset, limit);
        var page = new List<LocationDto>();
        long itemBytes = 0;
        var available = Math.Min(limit, Math.Max(0, results.Count - offset));
        for (var i = 0; i < available; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = results[offset + i];
            var candidateBytes = itemBytes + JsonSerializer.SerializeToUtf8Bytes(item, Compact).Length;
            var count = page.Count + 1;
            // Empty-array metadata plus the serialized items and their separating commas is
            // the exact compact JSON size. Each item is measured once, avoiding prefix reserialization.
            var metadata = SerializePage([], count, results.Count, offset, limit, summary);
            if (Encoding.UTF8.GetByteCount(metadata) + candidateBytes + count - 1 > _maxBytes)
            {
                if (page.Count == 0)
                {
                    throw new ArgumentException(
                        "A single reference exceeds the response byte budget. Retry with summary=true " +
                        $"or ask the operator to increase {EnvironmentVariableName}.");
                }

                break;
            }

            page.Add(item);
            itemBytes = candidateBytes;
        }

        return SerializePage(page, page.Count, results.Count, offset, limit, summary);
    }

    private static string SerializePage(
        IReadOnlyList<LocationDto> items, int count, int totalCount, int offset, int limit, bool summary)
    {
        var hasMore = offset + count < totalCount;
        return JsonSerializer.Serialize(new
        {
            count,
            totalCount,
            hasMore,
            offset,
            limit,
            summary,
            nextOffset = hasMore ? (int?)(offset + count) : null,
            items,
        }, Compact);
    }
}
