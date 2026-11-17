using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tests;

[TestClass]
public class ValidationIsolationContractTests
{
    [TestMethod]
    [DataRow(typeof(ValidationBundleToolsTests), nameof(ValidationBundleToolsTests.ResolveForkTtlHours_HonorsEnvOverrideAndFallsBack))]
    [DataRow(typeof(ValidationBundleToolsTests), nameof(ValidationBundleToolsTests.ResolveForkRestoreTimeoutMinutes_HonorsEnvOverrideAndFallsBack))]
    [DataRow(typeof(ValidationBundleToolsTests), nameof(ValidationBundleToolsTests.ResolveForkDotnetPath_HonorsEnvOverrideAndFallsBack))]
    [DataRow(typeof(DotnetCommandRunnerPipeLifetimeTests), nameof(DotnetCommandRunnerPipeLifetimeTests.CreateStartInfo_Strips_Inherited_MSBuild_Location_Variables))]
    public void ProcessEnvironmentWriter_IsExcludedFromParallelExecution(Type fixture, string methodName)
    {
        var method = fixture.GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsTrue(method.IsDefined(typeof(DoNotParallelizeAttribute), inherit: false),
            $"{fixture.Name}.{methodName} changes process environment visible to parallel fixtures.");
    }

    [TestMethod]
    public void PipeLifetimeFixture_DoesNotInvokeMachineWideBuildServerShutdown()
    {
        // Running the historical lifecycle to prove a failure would itself disrupt other
        // agents. Inspect actual invocations instead, and exercise owned pipes in the fixture.
        var path = Path.Combine(TestFixtureFileSystem.FindRepositoryRoot(),
            "tests", "RoslynMcp.Tests", "DotnetCommandRunnerPipeLifetimeTests.cs");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var literals = invocation.ArgumentList.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Select(literal => literal.Token.ValueText).ToArray();
            Assert.IsFalse(literals.Contains("build-server") && literals.Contains("shutdown"),
                "A pipe fixture must never shut down build servers owned by another agent.");
        }
    }
}
