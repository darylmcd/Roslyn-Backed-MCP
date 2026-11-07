using System.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

/// <summary>ADR 0033: what a composite-preview source fingerprint captures and when it reports drift.</summary>
[TestClass]
public sealed class CompositeSourceFingerprintTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(TestTempRoot.Current, "CompositeSourceFingerprint", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() => TestFixtureFileSystem.DeleteDirectoryIfExists(_dir);

    [TestMethod]
    public void Capture_RecordsExistingAsHashedAndMissingAsAbsent()
    {
        var existing = Path.Combine(_dir, "a.cs");
        var missing = Path.Combine(_dir, "b.cs");
        File.WriteAllBytes(existing, [1, 2, 3]);

        var captured = CompositeSourceFingerprint.Capture(
            [new CompositeFileMutation(existing, "x"), new CompositeFileMutation(missing, "y")]);

        Assert.AreEqual(2, captured.Count);
        Assert.IsTrue(captured[0].Exists);
        Assert.AreEqual("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", captured[0].Sha256);
        Assert.IsFalse(captured[1].Exists);
        Assert.IsNull(captured[1].Sha256);
    }

    [TestMethod]
    public void Capture_CountsPathsOnceByPlatformIdentity()
    {
        var upper = Path.Combine(_dir, "Feature.cs");
        var lower = Path.Combine(_dir, "feature.cs");
        var viaDotSegment = Path.Combine(_dir, ".", "Feature.cs");

        var captured = CompositeSourceFingerprint.Capture(
            [new CompositeFileMutation(upper, "1"), new CompositeFileMutation(lower, "2"), new CompositeFileMutation(viaDotSegment, "3")]);

        Assert.AreEqual(OperatingSystem.IsWindows() ? 1 : 2, captured.Count);
    }

    [TestMethod]
    public void FindDrift_ReportsOnlyTheFileName_AndTreatsBomOrEncodingChangesAsEdits()
    {
        var path = Path.Combine(_dir, "Encoded.cs");
        File.WriteAllText(path, "same text", new UTF8Encoding(false));
        var mutations = new[] { new CompositeFileMutation(path, "new text") };
        var fingerprints = CompositeSourceFingerprint.Capture(mutations);

        Assert.IsNull(CompositeSourceFingerprint.FindDrift(fingerprints, mutations));

        File.WriteAllText(path, "same text", new UTF8Encoding(true));

        var changed = CompositeSourceFingerprint.FindDrift(fingerprints, mutations);
        Assert.AreEqual("Encoded.cs", changed, "Same text with a BOM is different bytes, and only the file name may be reported.");
    }

    [TestMethod]
    public void FindDrift_TargetAlreadyInTheStateTheMutationProduces_IsNotDrift()
    {
        var written = Path.Combine(_dir, "written.cs");
        var removed = Path.Combine(_dir, "removed.cs");
        File.WriteAllText(written, "before");
        File.WriteAllText(removed, "before");
        var mutations = new[]
        {
            new CompositeFileMutation(written, "after"),
            new CompositeFileMutation(removed, null, DeleteFile: true),
        };
        var fingerprints = CompositeSourceFingerprint.Capture(mutations);

        File.WriteAllText(written, "after", new UTF8Encoding(true));
        File.Delete(removed);

        Assert.IsNull(CompositeSourceFingerprint.FindDrift(fingerprints, mutations),
            "A target the composite already brought to its final state is a retry, not someone else's edit.");

        File.WriteAllText(written, "after plus a hand edit");
        Assert.AreEqual("written.cs", CompositeSourceFingerprint.FindDrift(fingerprints, mutations));
    }
}
