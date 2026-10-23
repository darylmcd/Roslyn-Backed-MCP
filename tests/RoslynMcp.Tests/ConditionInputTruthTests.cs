using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ConditionInputTruthTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FileDirectorySuffix_ObservedTrueIsRefused_WithoutDependingOnOsColdQuery(bool alternateSeparator)
    {
        var path = Path.Combine(TestTempRoot.Current, "condition-truth", "marker");
        var query = path + (alternateSeparator ? Path.AltDirectorySeparatorChar : Path.DirectorySeparatorChar);
        var probe = new BuildPathRoles.ConditionProbe(path + ".proj", path, query, true);
        var file = new PhysicalInputIdentity(path, true, ImmutableArray.Create(new byte[32]));
        Assert.IsFalse(ConditionInputTruth.LogicalExists(query, PhysicalInputKind.File));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved(probe, PhysicalInputKind.File));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe], [file]));
    }

    [TestMethod]
    [DataRow("File", false, true)]
    [DataRow("File", true, false)]
    [DataRow("Directory", false, true)]
    [DataRow("Directory", true, true)]
    [DataRow("Missing", false, false)]
    [DataRow("Missing", true, false)]
    public void ExactPhysicalKindAndQueryIntent_AcceptsOnlyMatchingObservedTruth(string kindName, bool suffix, bool expected)
    {
        var kind = Enum.Parse<PhysicalInputKind>(kindName);
        var path = Path.Combine(TestTempRoot.Current, "condition-truth", "marker");
        var query = suffix ? path + Path.DirectorySeparatorChar : path;
        var probe = new BuildPathRoles.ConditionProbe(path + ".proj", path, query, expected);
        var identity = new PhysicalInputIdentity(path, kind != PhysicalInputKind.Missing,
            kind == PhysicalInputKind.File ? ImmutableArray.Create(new byte[32]) : []);
        Assert.AreEqual(expected, ConditionInputTruth.LogicalExists(query, kind));
        ConditionInputTruth.AssertObserved(probe, kind);
        ConditionInputTruth.AssertObserved([probe], [identity]);
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved(probe with { ObservedExists = !expected }, kind));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe with { ObservedExists = !expected }], [identity]));
    }

    [TestMethod]
    public void MatchingTruth_CannotSubstituteAnotherPhysicalInputOrMissingAuthority()
    {
        var path = Path.Combine(TestTempRoot.Current, "condition-truth", "marker");
        var probe = new BuildPathRoles.ConditionProbe(path + ".proj", path, path, false);
        var other = new PhysicalInputIdentity(path + ".other", false, []);
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved(probe with { QueryPath = other.FilePath }, PhysicalInputKind.Missing));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe], [other]));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe], []));
    }

    [TestMethod]
    public void ConflictingObservedTruths_CannotBeCollapsedIntoOneAcceptedProbe()
    {
        var path = Path.Combine(TestTempRoot.Current, "condition-truth", "marker");
        var probe = new BuildPathRoles.ConditionProbe(path + ".proj", path, path + Path.DirectorySeparatorChar, false);
        var file = new PhysicalInputIdentity(path, true, ImmutableArray.Create(new byte[32]));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe, probe with { ObservedExists = true }], [file]));
        Assert.ThrowsExactly<PublicInvalidOperationException>(() => ConditionInputTruth.AssertObserved([probe with { ObservedExists = true }, probe], [file]));
    }
}
