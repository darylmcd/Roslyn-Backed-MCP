using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeNormalizationBindingTests
{
    [TestMethod]
    [DataRow("split", false)]
    [DataRow("case", false)]
    [DataRow("order", true)]
    [DataRow("default-global", true)]
    [DataRow("same", true)]
    public void NativeCompilerReferenceKeys_PreserveAliasElementsAndExistingSetSemantics(string shape, bool equal)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeNormalizationBindingTests));
        var path = Path.Combine(root, "Controlled.dll");
        string[] left = shape == "split" ? ["a,b"] : shape == "default-global" ? [] : ["a", "b"];
        string[] right = shape switch { "case" => ["a", "B"], "order" => ["b", "a"], "default-global" => ["global"], _ => ["a", "b"] };
        var before = NativeCompilerTaskInputs.ReferenceKey(path, left, false, MetadataImageKind.Assembly, root);
        var after = NativeCompilerTaskInputs.ReferenceKey(path, right, false, MetadataImageKind.Assembly, root);
        Assert.AreEqual(equal, NativeCompilerTaskInputs.ReferenceIdentity.Comparer.Equals(before, after));
        Assert.AreEqual(equal, NativeCompilerTaskInputs.ReferenceIdentity.Comparer.Compare(before, after) == 0);
        Assert.AreEqual(equal, before.Equals(after));
        if (equal) Assert.AreEqual(before.GetHashCode(), after.GetHashCode());
    }

    [TestMethod]
    [DataRow("split")]
    [DataRow("join")]
    [DataRow("same")]
    [DataRow("embed")]
    public void NativeNormalizationBinding_PreservesTypedProjectEdgesAndExactAliasElements(string shape)
    {
        using var workspace = new AdhocWorkspace();
        var owner = ProjectId.CreateNewId();
        var target = ProjectId.CreateNewId();
        var originalAliases = shape == "join" || shape == "embed" ? ImmutableArray.Create("a", "b") : ImmutableArray.Create("a,b");
        var normalizedAliases = shape == "split" || shape == "embed" ? ImmutableArray.Create("a", "b") : ImmutableArray.Create("a,b");
        var original = workspace.CurrentSolution.AddProject(owner, "Owner", "Owner", LanguageNames.CSharp)
            .AddProject(target, "Target", "Target", LanguageNames.CSharp)
            .AddProjectReference(owner, new ProjectReference(target, originalAliases, false));
        var normalized = original.WithProjectReferences(owner, [new ProjectReference(target, normalizedAliases, shape == "embed")]);
        // Exercise the same selection invariant as owned publication, without
        // manufacturing native load evidence for this Adhoc alias-only fixture.
        if (shape is "split" or "join")
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => NativeCompilerContextCertificate.AssertNormalizationSelection(original, normalized));
        }
        else
        {
            NativeCompilerContextCertificate.AssertNormalizationSelection(original, normalized);
            Assert.AreEqual(shape == "embed", normalized.GetProject(owner)?.ProjectReferences.Single().EmbedInteropTypes);
        }
    }
}
