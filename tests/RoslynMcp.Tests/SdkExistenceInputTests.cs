using System.Reflection;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class SdkExistenceInputTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RegisteredSdkProbe_RequiresExactOriginAndCandidate_GrantsNoPackageContentAuthority(bool workloadPacks)
    {
        var sdk = BuildInputProvenance.RegisteredSdkDirectory();
        var parent = Path.GetDirectoryName(sdk);
        Assert.IsNotNull(parent);
        var installation = Path.GetDirectoryName(parent);
        Assert.IsNotNull(installation);
        var candidate = workloadPacks ? Path.Combine(installation, "library-packs") : Path.Combine(parent, "NuGetFallbackFolder");
        var origin = Path.Combine(sdk, "Sdks", "Microsoft.NET.Sdk", "targets", "Microsoft.NET.NuGetOfflineCache.targets");
        var observed = File.Exists(candidate) || Directory.Exists(candidate);
        var probe = new BuildPathRoles.ConditionProbe(origin, candidate, candidate, observed);
        Assert.IsTrue(SdkExistenceInput.Supports(probe));
        var inputs = SdkExistenceInput.Capture([probe], CancellationToken.None);
        Assert.HasCount(1, inputs);
        SdkExistenceInput.AssertCurrent(inputs, CancellationToken.None);
        var provenance = new ImmutablePathProvenance(BuildInputProvenance.ToolRoots(), [], []);
        Assert.IsFalse(provenance.IsImmutable(candidate));
        Assert.IsFalse(provenance.IsImmutable(Path.Combine(candidate, "package", "1.0.0", "lib.dll")));
        var forgedOrigin = new BuildPathRoles.ConditionProbe(Path.Combine(TestTempRoot.Current, "Forged.targets"), candidate, candidate, observed);
        var other = Path.Combine(TestTempRoot.Current, "unapproved-cache");
        var forgedCandidate = new BuildPathRoles.ConditionProbe(origin, other, other, false);
        var unobserved = new BuildPathRoles.ConditionProbe(origin, candidate, candidate, null);
        foreach (var forged in new[] { forgedOrigin, forgedCandidate, unobserved })
        {
            Assert.IsFalse(SdkExistenceInput.Supports(forged));
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => SdkExistenceInput.Capture([forged], CancellationToken.None));
        }
    }

    [TestMethod]
    public void MetadataOnlyProbe_CanInspectLockedRegularFileWithoutReadingItsContent()
    {
        var path = Path.Combine(TestTempRoot.Current, "metadata-only-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "owned regular-file sentinel");
        try
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var inspect = typeof(SdkExistenceInput).GetMethod("Inspect", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(inspect);
            // Exercise the exact metadata-only primitive, without issuing SDK authority
            // for this arbitrary fixture path or modifying the real SDK installation.
            var identity = inspect.Invoke(null, [path]);
            Assert.AreEqual((true, false), Assert.IsInstanceOfType<ValueTuple<bool, bool>>(identity));
        }
        finally { File.Delete(path); }
    }
}
