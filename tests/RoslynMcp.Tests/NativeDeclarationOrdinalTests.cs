using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using RoslynMcp.Roslyn;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeDeclarationOrdinalTests
{
    [TestMethod]
    public void DeclarationOrdinals_PreserveNativeNestedOrderAndDistinctSameLineElements()
    {
        MsBuildInitializer.EnsureInitialized();
        CheckOrdinals();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckOrdinals()
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(NativeDeclarationOrdinalTests), Guid.NewGuid().ToString("N"));
        NativeConditionEvidence? observer = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Owner.csproj");
            const string duplicate = "<PropertyGroup><OutputPath>bin/controlled/</OutputPath></PropertyGroup>";
            File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
                + duplicate + "<Target Name=\"NeverExecuted\"><PropertyGroup><OutputPath>bin/unexecuted/</OutputPath></PropertyGroup>"
                + "<ItemGroup><None Include=\"never.txt\"><Link>never.txt</Link></None></ItemGroup></Target>" + duplicate + "</Project>");
            observer = new NativeConditionEvidence();
            var owner = observer.Collection;
            owner.RegisterLogger(observer);
            observer.VerifyProtocol(owner);
            observer.Begin();
            var native = Project.FromProjectRootElement(ProjectRootElement.Open(path, owner, preserveFormatting: true),
                observer.Options(new Dictionary<string, string>(), owner));
            observer.Bind(native);
            var evidence = BuildPathRoles.Capture(native, observer);
            var authored = native.Xml.AllChildren.ToArray();
            var outputs = native.Xml.PropertyGroups.SelectMany(group => group.Properties).Where(property => property.Name == "OutputPath").ToArray();
            Assert.HasCount(2, outputs);
            Assert.AreEqual(outputs[0].Location.Line, outputs[1].Location.Line);
            Assert.AreEqual(outputs[0].OuterElement, outputs[1].OuterElement);
            var expected = outputs.Select(element => Array.FindIndex(authored, candidate => ReferenceEquals(candidate, element)) + 1).ToArray();
            Assert.IsTrue(expected[1] > expected[0] + 2, "Nested target descendants participate in the original AllChildren ordinals.");
            var actual = evidence.Declarations.Where(value => value.FilePath == path && value.Name == "OutputPath").ToArray();
            CollectionAssert.AreEqual(expected, actual.Select(value => value.Ordinal).ToArray());
            Assert.IsTrue(actual.All(value => value.Expression == "bin/controlled/"));
            var implicitImports = native.Imports.Select(import => import.ImportingElement)
                .Where(element => element.ImplicitImportLocation != ImplicitImportLocation.None).ToArray();
            Assert.IsTrue(implicitImports.Any(element => element.ImplicitImportLocation == ImplicitImportLocation.Top));
            Assert.IsTrue(implicitImports.Any(element => element.ImplicitImportLocation == ImplicitImportLocation.Bottom));
            foreach (var element in implicitImports)
                Assert.IsFalse(element.ContainingProject.AllChildren.Any(candidate => ReferenceEquals(candidate, element)));
            var roots = native.Imports.Select(import => import.ImportedProject).Append(native.Xml)
                .Distinct(ReferenceEqualityComparer.Instance).Cast<ProjectRootElement>().ToDictionary(root => root.FullPath);
            foreach (var declaration in evidence.Declarations)
            {
                var elements = roots[declaration.FilePath].AllChildren.ToArray();
                Assert.IsTrue(declaration.Ordinal >= 1 && declaration.Ordinal <= elements.Length,
                    "Implicit SDK imports must never acquire an invented authored ordinal.");
                Assert.AreEqual(declaration.Name, elements[declaration.Ordinal - 1].ElementName);
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native declaration fixture cleanup failed.", primary, observer,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }
}
