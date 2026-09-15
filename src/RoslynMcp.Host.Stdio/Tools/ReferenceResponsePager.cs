using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>Bounds decorated reference pages by their complete serialized UTF-8 payload.</summary>
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

    internal string Apply(string json)
    {
        // Ambiguous-symbol candidate envelopes have their own recovery shape and no reference items.
        if (JsonNode.Parse(json) is not JsonObject root || root["items"] is not JsonArray items)
            return json;

        var offset = root["offset"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Reference page has no offset.");
        var totalCount = root["totalCount"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Reference page has no total count.");
        var page = new JsonArray();
        root["items"] = new JsonArray();
        long itemBytes = 0;
        foreach (var item in items)
        {
            var candidateBytes = itemBytes + Encoding.UTF8.GetByteCount(item?.ToJsonString(Compact) ?? "null");
            var count = page.Count + 1;
            SetContinuation(root, count, offset, totalCount);
            // The empty-array envelope includes the final metadata. Add item bytes and commas
            // to measure the exact output without repeatedly serializing accepted prefixes.
            if (Encoding.UTF8.GetByteCount(root.ToJsonString(Compact)) + candidateBytes + count - 1 > _maxBytes)
            {
                if (page.Count == 0)
                    throw OversizedReference();
                break;
            }

            page.Add(item?.DeepClone());
            itemBytes = candidateBytes;
        }

        SetContinuation(root, page.Count, offset, totalCount);
        root["items"] = page;
        var bounded = root.ToJsonString(Compact);
        if (Encoding.UTF8.GetByteCount(bounded) > _maxBytes)
            throw OversizedReference();
        return bounded;
    }

    private static void SetContinuation(JsonObject root, int count, int offset, int totalCount)
    {
        var hasMore = (long)offset + count < totalCount;
        root["count"] = count;
        root["hasMore"] = hasMore;
        root["nextOffset"] = hasMore ? (int?)(offset + count) : null;
    }

    private static PublicArgumentException OversizedReference() => new(
        "A reference page cannot fit within the response byte budget. Retry with summary=true " +
        $"or ask the operator to increase {EnvironmentVariableName}.", "summary");
}
