using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class DocumentPathLookupTests
{
    [TestMethod]
    public async Task LinkedDocuments_RetainEveryExactPathEvenWhenContentsConflict()
    {
        using var workspace = new AdhocWorkspace();
        var (solution, path, upper, linked, lower) = CreateSolution(workspace);
        var ids = DocumentPathLookup.GetDocumentIds(solution, path);
        Assert.IsTrue(ids.Contains(upper));
        Assert.IsTrue(ids.Contains(linked));
        Assert.AreEqual(OperatingSystem.IsWindows(), ids.Contains(lower));
        var first = await solution.GetDocument(upper)!.GetTextAsync();
        var second = await solution.GetDocument(linked)!.GetTextAsync();
        Assert.IsFalse(first.ContentEquals(second), "Lookup must retain conflicting linked documents for the existing mutation refusal.");
    }

    [TestMethod]
    public void UnixCaseDistinctDocuments_AreSeparatePhysicalGroups()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix path identity control."); return; }
        using var workspace = new AdhocWorkspace();
        var (solution, path, upper, linked, lower) = CreateSolution(workspace);
        CollectionAssert.AreEquivalent(new[] { upper, linked }, DocumentPathLookup.GetDocumentIds(solution, path).ToArray());
        CollectionAssert.AreEqual(new[] { lower }, DocumentPathLookup.GetDocumentIds(solution, solution.GetDocument(lower)!.FilePath!).ToArray());
        Assert.IsTrue(DocumentPathLookup.GetDocumentIds(solution, Path.Combine(Path.GetDirectoryName(path)!, "REGISTRATION.cs")).IsEmpty);
    }

    [TestMethod]
    public void WindowsCaseVariants_KeepTheLinkedPhysicalGroup()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("Windows path identity control."); return; }
        using var workspace = new AdhocWorkspace();
        var (solution, path, upper, linked, lower) = CreateSolution(workspace);
        CollectionAssert.AreEquivalent(new[] { upper, linked, lower }, DocumentPathLookup.GetDocumentIds(solution, path).ToArray());
    }

    [TestMethod]
    public async Task CachedDiagnosticLocation_UsesPlatformPathIdentityAndCaseInsensitiveDiagnosticId()
    {
        using var workspace = new AdhocWorkspace();
        var upper = Path.Combine(Path.GetTempPath(), "document-path-lookup", "Registration.cs");
        var lower = Path.Combine(Path.GetTempPath(), "document-path-lookup", "registration.cs");
        var descriptor = new DiagnosticDescriptor("TEST001", "Test", "{0}", "Test", DiagnosticSeverity.Error, true);
        var line = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1));
        var first = Diagnostic.Create(descriptor, Location.Create(upper, new TextSpan(0, 1), line), "upper");
        var second = Diagnostic.Create(descriptor, Location.Create(lower, new TextSpan(0, 1), line), "lower");
        // A matching cached diagnostic returns before any compilation service is required.
        var lookup = new DiagnosticDocumentLookup(null!);
        var result = await lookup.FindAsync("path-lookup", workspace.CurrentSolution,
            new DiagnosticLookupTarget("test001", lower, 1, 1), [first, second], CancellationToken.None);
        Assert.AreSame(OperatingSystem.IsWindows() ? first : second, result.Diagnostic);
        Assert.IsNull(result.FullScanDiagnostics);
    }

    private static (Solution Solution, string Path, DocumentId Upper, DocumentId Linked, DocumentId Lower) CreateSolution(AdhocWorkspace workspace)
    {
        var solution = workspace.CurrentSolution;
        var ids = new List<DocumentId>();
        var path = Path.Combine(Path.GetTempPath(), "document-path-lookup", "Registration.cs");
        for (var index = 0; index < 3; index++)
        {
            var project = ProjectId.CreateNewId();
            var document = DocumentId.CreateNewId(project);
            ids.Add(document);
            solution = solution.AddProject(project, "Project" + index, "Assembly" + index, LanguageNames.CSharp)
                .AddDocument(document, "Registration.cs", SourceText.From("class Type" + index + " {}"),
                    filePath: index == 2 ? Path.Combine(Path.GetDirectoryName(path)!, "registration.cs") : path);
        }
        return (solution, path, ids[0], ids[1], ids[2]);
    }
}
