using System.Text;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct coverage for <see cref="SourceFileEncoding"/> — the BOM/encoding sniffer extracted from
/// <c>AtomicFileWriter</c> by <c>extract-atomicfilewriter-encoding-helper</c>. Before the
/// extraction this logic was reachable only through integration suites
/// (<c>ApplyTextEditVerifyTests</c>, <c>CsprojReserializationTests</c>, <c>UndoFileOperationsTests</c>),
/// so its edge cases — no-BOM UTF-8 must NOT normalize to the BOM-emitting <see cref="Encoding.UTF8"/>,
/// empty/short byte arrays, UTF-32LE/UTF-16LE BOM-prefix overlap — were only ever asserted
/// indirectly. These are unit tests against the helper itself.
/// </summary>
[TestClass]
public sealed class SourceFileEncodingTests
{
    // === Sniff ===

    [TestMethod]
    public void Sniff_NoBom_ReportsUtf8FallbackWithoutPreamble()
    {
        var bytes = Encoding.UTF8.GetBytes("<Project />");

        var (encoding, hasPreamble) = SourceFileEncoding.Sniff(bytes);

        // StreamReader's no-BOM fallback is the encoding it was constructed with — the
        // BOM-EMITTING Encoding.UTF8 — so HasPreamble is the load-bearing signal, not the
        // returned instance. Callers must gate on it (see FromBytes) or they would add a BOM.
        Assert.AreEqual(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.IsFalse(hasPreamble);
    }

    [TestMethod]
    public void Sniff_Utf8Bom_DetectsPreamble()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = WithPreamble(encoding, "<Project />");

        var (detected, hasPreamble) = SourceFileEncoding.Sniff(bytes);

        Assert.AreEqual(encoding.CodePage, detected.CodePage);
        Assert.IsTrue(hasPreamble);
    }

    [TestMethod]
    public void Sniff_Utf16Le_DetectsPreambleAndCodePage()
    {
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var bytes = WithPreamble(encoding, "<Project />");

        var (detected, hasPreamble) = SourceFileEncoding.Sniff(bytes);

        Assert.AreEqual(encoding.CodePage, detected.CodePage);
        Assert.IsTrue(hasPreamble);
    }

    [TestMethod]
    public void Sniff_Utf16Be_DetectsPreambleAndCodePage()
    {
        var encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        var bytes = WithPreamble(encoding, "<Project />");

        var (detected, hasPreamble) = SourceFileEncoding.Sniff(bytes);

        Assert.AreEqual(encoding.CodePage, detected.CodePage);
        Assert.IsTrue(hasPreamble);
    }

    [TestMethod]
    public void Sniff_Utf32Le_IsNotMisreadAsUtf16Le()
    {
        // The UTF-32LE BOM (FF FE 00 00) starts with the complete UTF-16LE BOM (FF FE). A
        // hand-rolled preamble table that tests UTF-16LE first silently mis-detects every
        // UTF-32LE file; delegating to StreamReader avoids that trap. This test is the guard.
        var encoding = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
        var bytes = WithPreamble(encoding, "<Project />");

        var (detected, hasPreamble) = SourceFileEncoding.Sniff(bytes);

        Assert.AreEqual(encoding.CodePage, detected.CodePage);
        Assert.AreNotEqual(Encoding.Unicode.CodePage, detected.CodePage);
        Assert.IsTrue(hasPreamble);
    }

    [TestMethod]
    public void Sniff_BytesShorterThanPreamble_ReportsNoPreamble()
    {
        // Two bytes cannot carry a three-byte UTF-8 BOM; the StartsWith guard must not throw.
        var (encoding, hasPreamble) = SourceFileEncoding.Sniff([0xEF, 0xBB]);

        Assert.AreEqual(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.IsFalse(hasPreamble);
    }

    [TestMethod]
    public void Sniff_EmptyBytes_ReportsNoPreamble()
    {
        var (encoding, hasPreamble) = SourceFileEncoding.Sniff([]);

        Assert.AreEqual(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.IsFalse(hasPreamble);
    }

    // === FromBytes ===

    [TestMethod]
    public void FromBytes_Null_ReturnsUtf8NoBom()
        => AssertIsUtf8NoBom(SourceFileEncoding.FromBytes(null));

    [TestMethod]
    public void FromBytes_Empty_ReturnsUtf8NoBom()
        => AssertIsUtf8NoBom(SourceFileEncoding.FromBytes([]));

    [TestMethod]
    public void FromBytes_NoBom_ReturnsUtf8NoBomNotBomEmittingUtf8()
    {
        // The regression this gate exists for: Sniff reports Encoding.UTF8 (BOM-emitting) for a
        // plain no-BOM file, so a pass-through would ADD a preamble to every no-BOM file.
        var resolved = SourceFileEncoding.FromBytes(Encoding.UTF8.GetBytes("a = b"));

        AssertIsUtf8NoBom(resolved);
    }

    [TestMethod]
    public void FromBytes_Utf8Bom_RoundTripsPreamble()
    {
        var original = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var resolved = SourceFileEncoding.FromBytes(WithPreamble(original, "a = b"));

        Assert.AreEqual(original.CodePage, resolved.CodePage);
        CollectionAssert.AreEqual(original.GetPreamble(), resolved.GetPreamble());
        // End-to-end: re-encoding through the resolved encoding reproduces the original bytes.
        CollectionAssert.AreEqual(WithPreamble(original, "a = b"), WithPreamble(resolved, "a = b"));
    }

    [TestMethod]
    public void FromBytes_Utf16Le_PreservesEncoding()
    {
        var original = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var resolved = SourceFileEncoding.FromBytes(WithPreamble(original, "a = b"));

        Assert.AreEqual(original.CodePage, resolved.CodePage);
        CollectionAssert.AreEqual(original.GetPreamble(), resolved.GetPreamble());
    }

    // === FromSourceText ===

    [TestMethod]
    public void FromSourceText_Null_ReturnsUtf8NoBom()
        => AssertIsUtf8NoBom(SourceFileEncoding.FromSourceText(null));

    [TestMethod]
    public void FromSourceText_RoslynThrowOnInvalidBytesUtf8_NormalizesToSharedUtf8NoBom()
    {
        // Roslyn attaches a no-BOM UTF-8 built with throwOnInvalidBytes: true. Passing it through
        // would turn an unpaired surrogate into a mid-write EncoderFallbackException where the
        // pre-fix path emitted U+FFFD, so it must normalize to the shared lenient instance.
        var roslynStyle = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        var resolved = SourceFileEncoding.FromSourceText(roslynStyle);

        Assert.AreSame(SourceFileEncoding.Utf8NoBom, resolved);
        // The normalization is observable: the shared instance substitutes rather than throws.
        Assert.AreEqual("�", resolved.GetString(resolved.GetBytes("\uD800")));
    }

    [TestMethod]
    public void FromSourceText_Utf8WithBom_IsPreserved()
    {
        var bomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        var resolved = SourceFileEncoding.FromSourceText(bomUtf8);

        Assert.AreSame(bomUtf8, resolved);
        Assert.AreNotEqual(0, resolved.GetPreamble().Length);
    }

    [TestMethod]
    public void FromSourceText_NonUtf8_IsPreserved()
    {
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        Assert.AreSame(utf16, SourceFileEncoding.FromSourceText(utf16));
    }

    // === Cross-check: the extracted sniffer agrees with CsprojSemanticEquality's snapshot ===

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void Sniff_AgreesWithCreateSnapshot(bool useUtf16, bool omitBom)
    {
        Encoding encoding = useUtf16
            ? new UnicodeEncoding(bigEndian: false, byteOrderMark: !omitBom)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: !omitBom);
        const string Content = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>";
        var bytes = WithPreamble(encoding, Content);

        var (sniffedEncoding, sniffedHasPreamble) = SourceFileEncoding.Sniff(bytes);
        var snapshot = CsprojSemanticEquality.CreateSnapshot(bytes);

        Assert.AreEqual(snapshot.TextEncoding.CodePage, sniffedEncoding.CodePage);
        Assert.AreEqual(snapshot.HasPreamble, sniffedHasPreamble);
        // CreateSnapshot now decodes with the sniffed encoding instead of re-detecting; the
        // content it produces must still be BOM-free and byte-faithful.
        Assert.AreEqual(Content, snapshot.Content);
    }

    private static byte[] WithPreamble(Encoding encoding, string content)
        => [.. encoding.GetPreamble(), .. encoding.GetBytes(content)];

    private static void AssertIsUtf8NoBom(Encoding encoding)
    {
        Assert.AreSame(SourceFileEncoding.Utf8NoBom, encoding);
        Assert.AreEqual(0, encoding.GetPreamble().Length);
    }
}
