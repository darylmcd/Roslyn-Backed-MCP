using System.Text;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Direct unit coverage for <c>FileSnapshotCapture</c>, the shared owner of the pre-apply
/// exists/missing snapshot policy previously hand-rolled at six call sites — <c>EditService</c>
/// twice, <c>EditorConfigService</c>, <c>ProjectMutationService</c>,
/// <c>RefactoringService.AddFileSnapshotAsync</c> and
/// <c>RefactoringService.AddProjectFileSnapshotAsync</c>
/// (<c>file-snapshot-capture-helper-consolidation</c>). Pure temp-directory tests — no MSBuild
/// workspace required.
/// </summary>
[TestClass]
public sealed class FileSnapshotCaptureTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "roslynmcp-snapshotcapture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }

    [TestMethod]
    public void FromBytesOrFallback_With_Bytes_Captures_Them_Byte_Exact()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };

        var snapshot = FileSnapshotCapture.FromBytesOrFallback("C:/x/y.cs", bytes, fallbackText: "ignored");

        Assert.AreEqual("C:/x/y.cs", snapshot.FilePath);
        CollectionAssert.AreEqual(bytes, snapshot.OriginalBytes.ToArray());
        Assert.AreEqual("hi", snapshot.OriginalText, "The BOM must be consumed when decoding, not surfaced as text.");
    }

    [TestMethod]
    public void FromBytesOrFallback_With_Empty_Bytes_Takes_The_Bytes_Path_Not_The_Fallback()
    {
        // An existing zero-length file yields an empty array, never null — it must NOT be
        // mistaken for a missing file, or revert would recreate it from the fallback text.
        var snapshot = FileSnapshotCapture.FromBytesOrFallback("C:/x/empty.cs", [], fallbackText: "not this");

        Assert.AreEqual(string.Empty, snapshot.OriginalText);
        Assert.IsTrue(snapshot.OriginalBytes.IsEmpty);
        Assert.IsFalse(snapshot.OriginalBytes.IsDefault, "Empty-but-present bytes must be recorded, not left default.");
    }

    [TestMethod]
    public void FromBytesOrFallback_With_Null_Bytes_Uses_The_Text_Fallback()
    {
        var snapshot = FileSnapshotCapture.FromBytesOrFallback("C:/x/new.cs", bytes: null, fallbackText: "pre-existing text");

        Assert.AreEqual("pre-existing text", snapshot.OriginalText);
        Assert.IsTrue(snapshot.OriginalBytes.IsDefault, "A file that never existed has no bytes to record.");
    }

    [TestMethod]
    public void FromBytesOrFallback_With_Null_Bytes_And_Null_Fallback_Yields_A_Null_Text_Snapshot()
    {
        var snapshot = FileSnapshotCapture.FromBytesOrFallback("C:/x/new.cs", bytes: null, fallbackText: null);

        Assert.IsNull(snapshot.OriginalText);
        Assert.IsTrue(snapshot.OriginalBytes.IsDefault);
    }

    [TestMethod]
    public async Task CaptureAsync_On_Existing_File_Reads_Bytes_And_Skips_The_Fallback_Factory()
    {
        var path = Path.Combine(_tempDir, "existing.cs");
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("class C {}")).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var factoryInvoked = false;

        var snapshot = await FileSnapshotCapture.CaptureAsync(
            path,
            () => { factoryInvoked = true; return "unused"; },
            CancellationToken.None);

        CollectionAssert.AreEqual(bytes, snapshot.OriginalBytes.ToArray(), "UTF-16 bytes must survive byte-exact.");
        Assert.AreEqual("class C {}", snapshot.OriginalText);
        Assert.IsFalse(factoryInvoked, "The fallback must stay lazy so the hot path never materializes a discarded string.");
    }

    [TestMethod]
    public async Task CaptureAsync_On_Existing_Empty_File_Records_Empty_Bytes()
    {
        var path = Path.Combine(_tempDir, "empty.cs");
        await File.WriteAllBytesAsync(path, []);

        var snapshot = await FileSnapshotCapture.CaptureAsync(path, () => "not this", CancellationToken.None);

        Assert.AreEqual(string.Empty, snapshot.OriginalText);
        Assert.IsFalse(snapshot.OriginalBytes.IsDefault);
    }

    [TestMethod]
    public async Task CaptureAsync_On_Missing_File_Uses_The_Fallback_Factory()
    {
        var path = Path.Combine(_tempDir, "missing.cs");

        var snapshot = await FileSnapshotCapture.CaptureAsync(path, () => "in-memory text", CancellationToken.None);

        Assert.AreEqual(path, snapshot.FilePath);
        Assert.AreEqual("in-memory text", snapshot.OriginalText);
        Assert.IsTrue(snapshot.OriginalBytes.IsDefault);
    }

    [TestMethod]
    public async Task CaptureAsync_On_Missing_File_With_No_Factory_Yields_A_Null_Text_Snapshot()
    {
        var path = Path.Combine(_tempDir, "missing.cs");

        var snapshot = await FileSnapshotCapture.CaptureAsync(path, fallbackTextFactory: null, CancellationToken.None);

        Assert.IsNull(snapshot.OriginalText);
        Assert.IsTrue(snapshot.OriginalBytes.IsDefault);
    }
}
