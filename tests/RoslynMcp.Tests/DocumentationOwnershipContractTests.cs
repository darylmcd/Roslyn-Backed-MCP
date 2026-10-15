using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DocumentationOwnershipContractTests
{
    [TestMethod]
    public void SourceComments_ResolveRepositoryPlanReferences()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var missing = new List<string>();
        foreach (var directory in new[] { "src", "analyzers", "tests" })
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/').Any(part => part is "bin" or "obj"))
                    continue;

                var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                foreach (var trivia in syntax.DescendantTrivia())
                {
                    if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                        && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                        && !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                        && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                        continue;

                    foreach (Match match in Regex.Matches(trivia.ToFullString(), @"ai_docs/plans/[A-Za-z0-9_./-]+\.md"))
                    {
                        if (!File.Exists(Path.Combine(root, match.Value)))
                            missing.Add($"{relative}: {match.Value}");
                    }
                }
            }
        }

        Assert.IsEmpty(missing, "Source comments must resolve their plan references: " + string.Join("; ", missing));
    }

    [TestMethod]
    public void ParameterObjectComments_KeepContractIndependentOfClosedBacklogDetails()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        var consumers = new[]
        {
            "src/RoslynMcp.Core/Models/ParameterObjectPreviewRequest.cs",
            "src/RoslynMcp.Core/Services/IParameterObjectService.cs",
            "src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs",
            "src/RoslynMcp.Host.Stdio/Tools/ParameterObjectTools.cs",
            "tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs",
        };

        foreach (var path in consumers)
        {
            Assert.IsFalse(File.ReadAllText(Path.Combine(root, path))
                .Contains("ai_docs/items/parameter-object-preview-design.md", StringComparison.Ordinal), path);
        }
    }

    [TestMethod]
    public void HookGuidance_DescribesAdvisoryHooksAndServerSideEnforcement()
    {
        var root = TestFixtureFileSystem.FindRepositoryRoot();
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "hooks", "hooks.json")));
        var hooks = configuration.RootElement.GetProperty("hooks");
        Assert.IsFalse(hooks.TryGetProperty("PreToolUse", out _));
        Assert.IsTrue(hooks.GetProperty("PostToolUse").GetArrayLength() > 0);

        foreach (var path in new[]
        {
            "src/RoslynMcp.Host.Stdio/README.md",
            "docs/reinstall.md",
            "ai_docs/architecture.md",
            "ai_docs/runtime.md",
        })
        {
            var text = File.ReadAllText(Path.Combine(root, path));
            Assert.IsTrue(text.Contains("advisory", StringComparison.Ordinal), path + ": describe advisory hooks");
            Assert.IsTrue(text.Contains("server-side", StringComparison.Ordinal), path + ": identify server-side enforcement");
            Assert.IsFalse(text.Contains("hooks enforce preview-before-apply", StringComparison.Ordinal), path);
            Assert.IsFalse(text.Contains("preview-before-apply guard", StringComparison.Ordinal), path);
            Assert.IsFalse(text.Contains("#plugin-hooks", StringComparison.OrdinalIgnoreCase), path);
        }
    }
}
