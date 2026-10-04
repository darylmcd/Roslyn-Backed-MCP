using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tests;

[TestClass]
public class ValidationIsolationContractTests
{
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
