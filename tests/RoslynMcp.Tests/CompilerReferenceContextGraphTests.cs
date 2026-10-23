using System.Runtime.CompilerServices;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerReferenceContextGraphTests
{
    [TestMethod]
    [DataRow("equivalent")]
    [DataRow("property-order")]
    [DataRow("property")]
    [DataRow("item-metadata")]
    [DataRow("reference-metadata")]
    [DataRow("item-definition")]
    [DataRow("target")]
    [DataRow("default-target")]
    [DataRow("writer-assembly")]
    [DataRow("using-task")]
    [DataRow("path-directory")]
    [DataRow("path-output-file")]
    [DataRow("path-output-coordinate")]
    [DataRow("path-native-input-directory")]
    [DataRow("path-input-coordinate")]
    [DataRow("path-input-file")]
    [DataRow("path-condition-query")]
    [DataRow("path-condition-truth")]
    [DataRow("path-declaration")]
    public void DuplicateContexts_RequireCompleteEvaluatedConsistency(string difference)
    {
        MsBuildInitializer.EnsureInitialized();
        CheckDuplicateContexts(difference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckDuplicateContexts(string difference)
    {
        var root = Path.Combine(TestTempRoot.Current, "compiler-context-duplicates-" + Guid.NewGuid().ToString("N"));
        var collections = new List<Microsoft.Build.Evaluation.ProjectCollection>();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            const string dependencyDefinition = """
                <Project DefaultTargets="Build">
                  <PropertyGroup><SemanticValue>original</SemanticValue><TargetPath>bin/Dependency.dll</TargetPath>
                    <MicrosoftNETBuildTasksAssembly>tools/Original.Tasks.dll</MicrosoftNETBuildTasksAssembly></PropertyGroup>
                  <ItemDefinitionGroup><Compile><Intent>default-original</Intent></Compile></ItemDefinitionGroup>
                  <ItemGroup><Compile Include="Dependency.cs"><Intent>item-original</Intent></Compile>
                    <ProjectReference Include="Leaf.csproj"><Aliases>reference-original</Aliases></ProjectReference></ItemGroup>
                  <Target Name="Build"><Message Text="target-original" /></Target>
                  <Target Name="Other" />
                  <UsingTask TaskName="FixtureNativeTask" AssemblyFile="tools/Original.Tasks.dll" />
                </Project>
                """;
            var secondDefinition = difference switch
            {
                "property" => dependencyDefinition.Replace(">original<", ">changed<", StringComparison.Ordinal),
                "item-metadata" => dependencyDefinition.Replace("item-original", "item-changed", StringComparison.Ordinal),
                "reference-metadata" => dependencyDefinition.Replace("reference-original", "reference-changed", StringComparison.Ordinal),
                "item-definition" => dependencyDefinition.Replace("default-original", "default-changed", StringComparison.Ordinal),
                "target" => dependencyDefinition.Replace("target-original", "target-changed", StringComparison.Ordinal),
                "default-target" => dependencyDefinition.Replace("DefaultTargets=\"Build\"", "DefaultTargets=\"Other\"", StringComparison.Ordinal),
                "writer-assembly" => dependencyDefinition.Replace("tools/Original.Tasks.dll", "tools/Other.Tasks.dll", StringComparison.Ordinal),
                "using-task" => dependencyDefinition.Replace("AssemblyFile=\"tools/Original.Tasks.dll\"", "AssemblyFile=\"tools/Other.Tasks.dll\"", StringComparison.Ordinal),
                _ => dependencyDefinition
            };
            Microsoft.Build.Execution.ProjectInstance Evaluate(string name, string xml)
            {
                var path = Path.Combine(root, name);
                File.WriteAllText(path, xml);
                // A distinct collection reads each physical definition, including
                // the deliberately changed snapshot of the same dependency path.
                var collection = new Microsoft.Build.Evaluation.ProjectCollection();
                collections.Add(collection);
                return new Microsoft.Build.Execution.ProjectInstance(path, new Dictionary<string, string>(), null, collection);
            }
            var owner = Evaluate("Owner.csproj", """
                <Project><ItemGroup><ProjectReference Include="Dependency.csproj" /></ItemGroup></Project>
                """);
            var dependency = Evaluate("Dependency.csproj", dependencyDefinition);
            var duplicate = Evaluate("Dependency.csproj", secondDefinition);
            var leaf = Evaluate("Leaf.csproj", "<Project />");
            var registrations = new Dictionary<Microsoft.Build.Execution.ProjectInstance, NativeTaskRegistrations>(ReferenceEqualityComparer.Instance);
            foreach (var instance in new[] { owner, dependency, duplicate, leaf })
                registrations.Add(instance, NativeTaskRegistrations.Capture(instance, CancellationToken.None));
            Assert.AreEqual(root, owner.Directory);
            Assert.AreEqual(dependency.FullPath, owner.GetItems("ProjectReference").Single().GetMetadataValue("FullPath"));
            Assert.AreEqual(leaf.FullPath, dependency.GetItems("ProjectReference").Single().GetMetadataValue("FullPath"));
            if (difference == "property-order")
            {
                dependency.SetProperty("Alpha", "same-alpha");
                dependency.SetProperty("Beta", "same-beta");
                duplicate.SetProperty("Beta", "same-beta");
                duplicate.SetProperty("Alpha", "same-alpha");
            }
            BuildPathRoles.Evidence Evidence(Microsoft.Build.Execution.ProjectInstance instance)
            {
                var marker = Path.Combine(root, "marker.flag");
                var original = new BuildPathRoles.Evidence([], [], [], [], [], [],
                    [new(dependency.FullPath, marker, marker, false)], [], []);
                if (!ReferenceEquals(instance, duplicate)) return original;
                return difference switch
                {
                    "path-directory" => original with { Directories = [root] },
                    "path-output-file" => original with { OutputFiles = [marker] },
                    "path-output-coordinate" => original with { OutputCoordinates = [root] },
                    "path-native-input-directory" => original with { NativeInputDirectories = [root] },
                    "path-input-coordinate" => original with { InputCoordinates = [new(dependency.FullPath, root, marker)] },
                    "path-input-file" => original with { InputFiles = [marker] },
                    "path-condition-query" => original with { ConditionProbes = [new(dependency.FullPath, marker, marker + Path.DirectorySeparatorChar, false)] },
                    "path-condition-truth" => original with { ConditionProbes = [new(dependency.FullPath, marker, marker, true)] },
                    "path-declaration" => original with { Declarations = [new(dependency.FullPath, 1, "OutputPath", null, "bin", "bin", [])] },
                    _ => original
                };
            }
            if (difference is "equivalent" or "property-order")
            {
                var contexts = CompilerReferenceContextGraph.ValidateContexts([owner, dependency, duplicate, leaf], Evidence,
                    instance => registrations[instance], CancellationToken.None);
                var targets = CompilerReferenceContextGraph.Capture(owner, contexts, CancellationToken.None);
                Assert.HasCount(1, targets);
                Assert.HasCount(1, targets[0]);
                Assert.AreEqual(dependency.FullPath, targets[0][0].Path);
                Assert.AreEqual(Path.Combine(root, "bin", "Dependency.dll"), targets[0][0].Output);
                Assert.IsNull(CompilerProjectReferences.Capture(owner, targets).MicrosoftNetBuildTasksAssembly);
                var dependencyTargets = CompilerReferenceContextGraph.Capture(dependency, contexts, CancellationToken.None);
                Assert.AreEqual(Path.Combine(root, "tools", "Original.Tasks.dll"),
                    CompilerProjectReferences.Capture(dependency, dependencyTargets).MicrosoftNetBuildTasksAssembly);
            }
            else
            {
                Assert.ThrowsExactly<PublicInvalidOperationException>(() =>
                    CompilerReferenceContextGraph.ValidateContexts([owner, dependency, duplicate, leaf], Evidence,
                        instance => registrations[instance], CancellationToken.None));
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Duplicate compiler context fixture cleanup failed.", primary,
                collections.Cast<IDisposable>().Append(OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root))).ToArray());
        }
    }
}
