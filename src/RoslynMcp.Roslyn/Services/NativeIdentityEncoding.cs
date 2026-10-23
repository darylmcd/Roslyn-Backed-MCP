using System.Security.Cryptography;
using System.Text;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Length-framed exact .NET string identity; no text replacement or normalization.</summary>
internal static class NativeIdentityEncoding
{
    internal static string ReadString(BinaryReader reader, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = reader.ReadInt32();
        if (length < 0 || !reader.BaseStream.CanSeek || length > (reader.BaseStream.Length - reader.BaseStream.Position) / sizeof(ushort))
            throw new InvalidDataException("Invalid exact string frame.");
        var codeUnits = new char[length];
        for (var index = 0; index < codeUnits.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            codeUnits[index] = (char)reader.ReadUInt16();
        }
        var result = new string(codeUnits);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    internal static void WriteString(BinaryWriter writer, string value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        writer.Write(value.Length);
        Span<byte> buffer = stackalloc byte[1024];
        var used = 0;
        foreach (var codeUnit in value)
        {
            ct.ThrowIfCancellationRequested();
            buffer[used++] = (byte)codeUnit;
            buffer[used++] = (byte)(codeUnit >> 8);
            if (used != buffer.Length) continue;
            writer.Write(buffer);
            used = 0;
        }
        if (used != 0) writer.Write(buffer[..used]);
        ct.ThrowIfCancellationRequested();
    }

    internal static string StringHash(string value, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        // Only numeric primitives are written; WriteString preserves code units.
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) WriteString(writer, value, ct);
        ct.ThrowIfCancellationRequested();
        var result = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
