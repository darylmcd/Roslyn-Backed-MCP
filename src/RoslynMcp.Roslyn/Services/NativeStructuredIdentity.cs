using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Structural identity with exact framed UTF-16 strings, including dictionary keys.</summary>
internal static class NativeStructuredIdentity
{
    internal static string Serialize<T>(T value, CancellationToken ct)
        => new Session(ct).Serialize(value);

    /// <summary>One operation's serializer metadata cache; cancellation never escapes that operation.</summary>
    internal sealed class Session
    {
        private readonly CancellationToken _ct;
        private readonly JsonSerializerOptions _options;
        internal Session(CancellationToken ct)
        {
            _ct = ct;
            _options = CreateStringOptions(ct);
            _options.Converters.Add(new OrderedDictionaryConverterFactory(ct));
        }
        internal string Serialize<T>(T value)
        {
            _ct.ThrowIfCancellationRequested();
            var result = JsonSerializer.Serialize(value, _options);
            _ct.ThrowIfCancellationRequested();
            return result;
        }
    }

    internal static JsonSerializerOptions CreateStringOptions(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ExactStringConverter(ct));
        return options;
    }

    internal static string StringToken(string value, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            NativeIdentityEncoding.WriteString(writer, value, ct);
        ct.ThrowIfCancellationRequested();
        return EncodeBytes(stream.ToArray());
    }

    // Padded Base64URL keeps the private JSON wire alphabet ASCII-safe without
    // relaxing JSON escaping; padding and the exact length frame are mandatory.
    private static string EncodeBytes(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');

    private static string ReadToken(string token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/'));
            if (!string.Equals(EncodeBytes(bytes), token, StringComparison.Ordinal))
                throw new JsonException("A native string token is not canonical.");
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var value = NativeIdentityEncoding.ReadString(reader, ct);
            if (stream.Position != stream.Length) throw new JsonException("A native string token has trailing data.");
            return value;
        }
        catch (Exception failure) when (failure is FormatException or InvalidDataException or EndOfStreamException)
        {
            throw new JsonException("A native string token is malformed.");
        }
    }

    private sealed class ExactStringConverter(CancellationToken ct) : JsonConverter<string>
    {
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(StringToken(value, ct));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(StringToken(value, ct));
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? ReadToken(reader.GetString()!, ct)
                : throw new JsonException("A native string token must be a string.");
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadToken(reader.GetString()!, ct);
    }

    private sealed class OrderedDictionaryConverterFactory(CancellationToken ct) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(IReadOnlyDictionary<string, string>).IsAssignableFrom(typeToConvert)
            || typeof(IDictionary<string, string>).IsAssignableFrom(typeToConvert);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(OrderedDictionaryConverter<>).MakeGenericType(typeToConvert), [ct])!;
    }

    private sealed class OrderedDictionaryConverter<T>(CancellationToken ct) : JsonConverter<T>
        where T : IEnumerable<KeyValuePair<string, string>>
    {
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                writer.WritePropertyName(StringToken(pair.Key, ct));
                writer.WriteStringValue(StringToken(pair.Value, ct));
            }
            writer.WriteEndObject();
        }
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("Native structural identities are write-only.");
    }
}
