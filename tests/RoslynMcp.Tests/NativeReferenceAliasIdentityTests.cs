using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeReferenceAliasIdentityTests
{
    [TestMethod]
    [DataRow("metadata", "split")]
    [DataRow("evaluated", "split")]
    [DataRow("source", "split")]
    [DataRow("context", "split")]
    [DataRow("shared", "split")]
    [DataRow("metadata", "case")]
    [DataRow("evaluated", "case")]
    [DataRow("source", "case")]
    [DataRow("context", "case")]
    [DataRow("shared", "case")]
    [DataRow("metadata", "order")]
    [DataRow("evaluated", "order")]
    [DataRow("source", "order")]
    [DataRow("context", "order")]
    [DataRow("shared", "order")]
    [DataRow("metadata", "default-global")]
    [DataRow("evaluated", "default-global")]
    [DataRow("source", "default-global")]
    [DataRow("context", "default-global")]
    [DataRow("shared", "default-global")]
    [DataRow("metadata", "same")]
    [DataRow("evaluated", "same")]
    [DataRow("source", "same")]
    [DataRow("context", "same")]
    [DataRow("shared", "same")]
    public void PhysicalGraphReferenceIdentities_PreserveAliasElementsAndExistingNormalization(string family, string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeReferenceAliasIdentityTests));
        using var workspace = new AdhocWorkspace();
        var target = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(target, "Target", "Target", LanguageNames.CSharp)
            .WithProjectFilePath(target, Path.Combine(root, "Target.csproj"));
        ImmutableArray<string> left = shape == "split" ? ImmutableArray.Create("a,b") : shape == "default-global" ? [] : ImmutableArray.Create("a", "b");
        var right = shape switch
        {
            "case" => ImmutableArray.Create("a", "B"),
            "order" => ImmutableArray.Create("b", "a"),
            "default-global" => ImmutableArray.Create("global"),
            _ => ImmutableArray.Create("a", "b")
        };
        var before = Key(left);
        var after = Key(right);
        var equal = shape is "same" or "order" || shape == "default-global" && family != "metadata";
        Assert.AreEqual(equal, StringComparer.Ordinal.Equals(before, after));

        string Key(ImmutableArray<string> aliases)
        {
            Func<string, string> map = static path => path;
            var reference = new ProjectReference(target, aliases, false);
            if (family == "shared") return SolutionFileMutationPlanner.ReferenceMetadataKey(aliases, false);
            var name = family switch { "metadata" => "MetadataKey", "evaluated" => "EvaluatedReferenceKey", "source" => "ReferenceKey", "context" => "ReferenceContextKey", _ => throw new AssertFailedException("Unknown controlled identity family.") };
            var method = typeof(SolutionFileMutationPlanner).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            object[] arguments = family switch
            {
                "metadata" => [MetadataReference.CreateFromFile(typeof(object).Assembly.Location, new MetadataReferenceProperties(MetadataImageKind.Assembly, aliases)), map],
                "evaluated" => [new EvaluatedProjectGraph.Reference(Path.Combine(root, "Target.csproj"), Path.Combine(root, "Owner.csproj"), "Target.csproj", false, aliases, 1)],
                _ => [solution, reference, map]
            };
            return method.Invoke(null, arguments) as string ?? throw new AssertFailedException("A physical reference identity must be a string.");
        }
    }
}
