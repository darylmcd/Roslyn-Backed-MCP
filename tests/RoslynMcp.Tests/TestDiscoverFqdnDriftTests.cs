using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// test-run-fqdn-drift-vs-test-discover: covers the fully-qualified-name composition path in
/// <see cref="TestDiscoveryService"/>. Before the fix, the FQDN was synthesized as
/// <c>{projectName}.{class}.{method}</c>; that produced silent zero-hits when a test class
/// lived inside a sub-namespace (e.g. <c>MyProject.SubFolder.MyTestClass</c> in a project
/// named <c>MyProject</c>) because <c>dotnet test --filter "FullyQualifiedName~..."</c> with
/// the project-name prefix never matched the actual test-runner-registered name (which uses
/// the declared namespace). The fix reads the declared namespace from the syntax tree.
///
/// These tests drive the production helper directly (it is <c>internal static</c> and
/// <c>RoslynMcp.Roslyn</c> grants <c>InternalsVisibleTo("RoslynMcp.Tests")</c>) so they run
/// in milliseconds without needing an MSBuild workspace. The disk-backed end-to-end behaviour
/// is already exercised by <c>ValidationIntegrationTests.Test_Discover_Returns_Test_Methods_For_Test_Project</c>,
/// which continues to pass because <c>SampleLib.Tests</c> happens to use a namespace identical
/// to its project name.
/// </summary>
[TestClass]
public class TestDiscoverFqdnDriftTests
{
    [TestMethod]
    public void BuildFullyQualifiedTestName_UsesDeclaredSubNamespace_NotProjectName()
    {
        // Project name "SampleLib.Tests" but class declared inside the "Unit" sub-namespace.
        // The runner registers this as "SampleLib.Tests.Unit.AnimalServiceTests.CountAnimals",
        // so test_discover MUST emit that — not "SampleLib.Tests.AnimalServiceTests.CountAnimals".
        var method = ParseTestMethod(
            """
            namespace SampleLib.Tests.Unit;

            [TestClass]
            public class AnimalServiceTests
            {
                [TestMethod]
                public void CountAnimals() { }
            }
            """);

        var fqn = TestDiscoveryService.BuildFullyQualifiedTestName("SampleLib.Tests", method);

        Assert.AreEqual("SampleLib.Tests.Unit.AnimalServiceTests.CountAnimals", fqn);
    }

    [TestMethod]
    public void BuildFullyQualifiedTestName_ClassicNamespace_UsesDeclaredNamespace()
    {
        // Classic block-scoped namespace; same expectation as the file-scoped case.
        var method = ParseTestMethod(
            """
            namespace MyProject.Integration
            {
                public class WidgetTests
                {
                    [TestMethod]
                    public void Widget_Builds() { }
                }
            }
            """);

        var fqn = TestDiscoveryService.BuildFullyQualifiedTestName("MyProject", method);

        Assert.AreEqual("MyProject.Integration.WidgetTests.Widget_Builds", fqn);
    }

    [TestMethod]
    public void BuildFullyQualifiedTestName_NestedNamespaces_ConcatenatedOuterToInner()
    {
        // Nested `namespace A { namespace B { ... } }` should produce "A.B.Class.Method",
        // not "B.Class.Method" (innermost only) and not "B.A.Class.Method" (reversed order).
        var method = ParseTestMethod(
            """
            namespace Outer
            {
                namespace Inner
                {
                    public class NestedTests
                    {
                        [TestMethod]
                        public void Nested_Works() { }
                    }
                }
            }
            """);

        var fqn = TestDiscoveryService.BuildFullyQualifiedTestName("SomeProject", method);

        Assert.AreEqual("Outer.Inner.NestedTests.Nested_Works", fqn);
    }

    [TestMethod]
    public void BuildFullyQualifiedTestName_GlobalNamespace_FallsBackToProjectName()
    {
        // No enclosing namespace declaration → fall back to the MSBuild project name so we
        // never emit a fragment like ".ClassName.Method" with an empty prefix.
        var method = ParseTestMethod(
            """
            public class GlobalTests
            {
                [TestMethod]
                public void Global_Works() { }
            }
            """);

        var fqn = TestDiscoveryService.BuildFullyQualifiedTestName("MyProject", method);

        Assert.AreEqual("MyProject.GlobalTests.Global_Works", fqn);
    }

    [TestMethod]
    public void GetEnclosingNamespace_GlobalNamespace_ReturnsNull()
    {
        // Direct helper check: confirms the null-namespace branch the fallback relies on.
        var method = ParseTestMethod(
            """
            public class C
            {
                [TestMethod]
                public void M() { }
            }
            """);

        Assert.IsNull(TestDiscoveryService.GetEnclosingNamespace(method));
    }

    [TestMethod]
    public void GetEnclosingNamespace_FileScopedNamespace_ReturnsName()
    {
        var method = ParseTestMethod(
            """
            namespace Acme.Tooling.Tests;

            public class T
            {
                [TestMethod]
                public void M() { }
            }
            """);

        Assert.AreEqual("Acme.Tooling.Tests", TestDiscoveryService.GetEnclosingNamespace(method));
    }

    [TestMethod]
    public void SynthesizeDotnetTestFilter_AcceptsSubNamespaceFqn_WithoutSilentZeroHits()
    {
        // End-to-end: when the FQDN is correctly built from a sub-namespace declaration,
        // the dotnet-test filter string must include that sub-namespace prefix verbatim —
        // otherwise `dotnet test --filter` passes but matches zero tests at runtime.
        var method = ParseTestMethod(
            """
            namespace SampleLib.Tests.Unit;

            public class AnimalServiceTests
            {
                [TestMethod]
                public void CountAnimals() { }
            }
            """);

        var fqn = TestDiscoveryService.BuildFullyQualifiedTestName("SampleLib.Tests", method);
        var filter = TestDiscoveryService.SynthesizeDotnetTestFilter([fqn]);

        Assert.AreEqual("FullyQualifiedName~SampleLib.Tests.Unit.AnimalServiceTests.CountAnimals", filter);
    }

    private static MethodDeclarationSyntax ParseTestMethod(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
    }
}
