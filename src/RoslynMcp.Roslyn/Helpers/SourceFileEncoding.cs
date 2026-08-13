using System.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shared BOM/encoding detection for every path that rewrites a file on disk and must keep the
/// file's original encoding byte-faithful (<c>mutation-write-paths-drop-original-encoding</c>).
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>AtomicFileWriter</c> (<c>extract-atomicfilewriter-encoding-helper</c>).
/// Encoding sniffing is not atomic-write plumbing, yet five non-atomic-write call sites —
/// <c>EditorConfigService</c> (which writes via <see cref="File.WriteAllLines(string, IEnumerable{string}, Encoding)"/>),
/// <c>ProjectMutationService</c>, <c>EditService</c>, <c>UndoService</c>, and
/// <c>CompositeApplyOrchestrator</c> — depended on <c>AtomicFileWriter</c> purely as a sniffer.
/// That inverted dependency also made <see cref="CsprojSemanticEquality"/> (scoped by its own doc
/// header to csproj-XML semantic comparison) the de-facto BOM detector for <c>.editorconfig</c>
/// and every mutation-write path. Detection now lives here; <c>CsprojSemanticEquality</c> is a
/// consumer like everyone else.
/// </para>
/// <para>
/// Detection is BOM-based only, delegated to <see cref="StreamReader"/>'s own byte-order-mark
/// detection rather than a hand-rolled preamble table (a manual table has to get the
/// UTF-32LE/UTF-16LE BOM-prefix overlap right; the BCL already does). A BOM-less UTF-16 or
/// codepage file is indistinguishable from UTF-8 here and is rewritten as UTF-8-no-BOM, matching
/// the pre-extraction behavior.
/// </para>
/// </remarks>
internal static class SourceFileEncoding
{
    /// <summary>
    /// UTF-8 without a byte-order mark — the encoding .NET's <c>Encoding</c>-less
    /// <see cref="File.WriteAllTextAsync(string, string?, CancellationToken)"/> overload uses.
    /// Also the correct default for a brand-new file and for any source whose original encoding
    /// could not be determined, so omitting <c>encoding</c> preserves the pre-existing behavior
    /// byte-for-byte. Deliberately NOT <see cref="Encoding.UTF8"/>: that static instance emits a
    /// BOM, so using it would ADD a preamble to every no-BOM file.
    /// </summary>
    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Detects the encoding of <paramref name="bytes"/> from its byte-order mark, reporting both
    /// the detected <see cref="Encoding"/> and whether the bytes actually carry that encoding's
    /// preamble.
    /// </summary>
    /// <remarks>
    /// The two results are NOT redundant. With no BOM present <see cref="StreamReader"/> falls back
    /// to the encoding it was constructed with — <see cref="Encoding.UTF8"/>, which is
    /// BOM-<em>emitting</em> — so callers that want to re-emit the original preamble must gate on
    /// <c>HasPreamble</c> rather than trusting the detected instance (see <see cref="FromBytes"/>).
    /// Only <see cref="TextReader.Peek"/> is called: that forces <see cref="StreamReader"/>'s lazy
    /// BOM detection on its first internal buffer fill WITHOUT decoding the file to EOF, so callers
    /// that only want the encoding no longer pay for a full-file decode.
    /// </remarks>
    internal static (Encoding Encoding, bool HasPreamble) Sniff(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        reader.Peek();
        var encoding = reader.CurrentEncoding;
        var preamble = encoding.GetPreamble();
        var hasPreamble = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        return (encoding, hasPreamble);
    }

    /// <summary>
    /// Resolves the <see cref="Encoding"/> a file should be REWRITTEN with, given the bytes it
    /// held before the mutation. Returns <see cref="Utf8NoBom"/> when <paramref name="existingBytes"/>
    /// is <see langword="null"/> or empty (the file is being created) or when the original carried
    /// no preamble. Otherwise returns the BOM-derived encoding so the preamble is re-emitted verbatim.
    /// </summary>
    /// <remarks>
    /// The <c>HasPreamble</c> gate is load-bearing. <see cref="Sniff"/> reports
    /// <see cref="Encoding.UTF8"/> (BOM-emitting) for a plain no-BOM UTF-8 file, because that is the
    /// <see cref="StreamReader"/> fallback when no byte-order mark is detected. Writing with that
    /// instance would add a BOM to every no-BOM file, so the no-preamble case must fall back to
    /// <see cref="Utf8NoBom"/> instead of trusting the detected encoding.
    /// </remarks>
    internal static Encoding FromBytes(byte[]? existingBytes)
    {
        if (existingBytes is null || existingBytes.Length == 0)
        {
            return Utf8NoBom;
        }

        var (encoding, hasPreamble) = Sniff(existingBytes);
        return hasPreamble ? encoding : Utf8NoBom;
    }

    /// <summary>
    /// Resolves the <see cref="Encoding"/> to write with from an encoding Roslyn already attached
    /// to a <c>SourceText</c> (<c>SourceText.Encoding</c>), which is <see langword="null"/> for text
    /// synthesized in memory.
    /// </summary>
    /// <remarks>
    /// A UTF-8 encoding that emits no preamble is normalized to the shared <see cref="Utf8NoBom"/>
    /// instance rather than passed through: Roslyn's own no-BOM UTF-8 instance is constructed with
    /// <c>throwOnInvalidBytes: true</c>, so writing through it would turn an unpaired surrogate into
    /// a mid-write <see cref="EncoderFallbackException"/> where the pre-fix path silently emitted
    /// U+FFFD. Non-UTF-8 and BOM-carrying encodings are preserved as-is.
    /// </remarks>
    internal static Encoding FromSourceText(Encoding? sourceEncoding)
    {
        if (sourceEncoding is null)
        {
            return Utf8NoBom;
        }

        return sourceEncoding.CodePage == Utf8NoBom.CodePage && sourceEncoding.GetPreamble().Length == 0
            ? Utf8NoBom
            : sourceEncoding;
    }
}
