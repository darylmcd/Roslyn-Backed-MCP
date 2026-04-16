namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for the bundled fix:
///   - dr-9-7-produces-output-that-violates-project-formatting (IT-Chat-Bot audit §9.7):
///     extract_method_preview emitted `var x=Method(a,b);` (no spaces around `=` or `,`),
///     a method declaration at column 1 (no class-scope indent), and collapsed multi-line
///     LINQ chains onto a single physical line.
///   - dr-9-9-format-bug-004-produces-malformed-body-closing-b (Firewall-Analyzer audit §9.9):
///     extract_method_preview produced `}}` glued together at the end of the synthesized
///     method, body statements at column 1 with lost indentation, and a stray trailing `-}`.
///
/// Both bugs share a single root cause in
/// <see cref="RoslynMcp.Roslyn.Services.ExtractMethodService.PreviewExtractMethodAsync"/>:
/// raw factory-built nodes plus a <c>NormalizeWhitespace()</c> pass on the new method node
/// in isolation, with no document-wide formatter pass to apply project editorconfig.
/// The fix annotates the synthesized method and call statement with
/// <see cref="Microsoft.CodeAnalysis.Formatting.Formatter.Annotation"/> and routes the
/// result through <see cref="Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(Microsoft.CodeAnalysis.Document, Microsoft.CodeAnalysis.SyntaxAnnotation, Microsoft.CodeAnalysis.Options.OptionSet?, System.Threading.CancellationToken)"/>.
/// </summary>
[TestClass]
public sealed class ExtractMethodFormatRegressionTests : TestBase
{
    [ClassInitialize]
    public static void ClassInit(TestContext _) => InitializeServices();

    [ClassCleanup]
    public static void ClassCleanup() => DisposeServices();

    /// <summary>
    /// dr-9-7: the call site must use the project's editorconfig spacing (`var x = M(a, b);`),
    /// the synthesized method declaration must be indented to match its sibling members,
    /// and the body must NOT be glued onto the declaration line.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_RespectsProjectFormatting_AtCallSiteAndDeclaration()
    {
        var (workspaceId, fixturePath, solutionDir) = await CreateFixtureAsync(
            "Dr97FormatHost.cs",
            """
            namespace SampleLib;

            public class Dr97FormatHost
            {
                public int Compute(int a, int b)
                {
                    var sum = a + b;
                    var doubled = sum * 2;
                    return doubled;
                }
            }
            """);

        try
        {
            // Extract the two `var` declarations into ComputeIntermediate. `doubled` flows
            // out (used in the return on the next line) so the call site is `var doubled = M(a, b);`.
            var preview = await ExtractMethodService.PreviewExtractMethodAsync(
                workspaceId,
                fixturePath,
                startLine: 7, startColumn: 9,
                endLine: 8, endColumn: 31,
                methodName: "ComputeIntermediate",
                ct: CancellationToken.None);

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(apply.Success, $"Apply must succeed. Error: {apply.Error}");

            var updated = await File.ReadAllTextAsync(fixturePath);

            // 9.7 shape A: editorconfig-correct spacing at the call site.
            Assert.IsTrue(
                updated.Contains("var doubled = ComputeIntermediate(", StringComparison.Ordinal),
                $"Call site must have a single space around `=`. Got:\n{updated}");
            Assert.IsFalse(
                updated.Contains("var doubled=ComputeIntermediate(", StringComparison.Ordinal),
                $"Regression: call site missing spaces around `=`. Got:\n{updated}");

            // 9.7 shape B: argument list spacing.
            Assert.IsTrue(
                updated.Contains("ComputeIntermediate(a, b)", StringComparison.Ordinal),
                $"Argument list must have a space after `,`. Got:\n{updated}");

            // 9.7 shape C: synthesized method is indented (4 spaces inside the class scope).
            // The `private` keyword must NOT be at column 1 — it must sit at the class body's
            // indent level (4 spaces in this fixture).
            Assert.IsFalse(
                updated.Contains("\nprivate int ComputeIntermediate", StringComparison.Ordinal),
                $"Regression: synthesized method declared at column 1 with no class-scope indent. Got:\n{updated}");
            Assert.IsTrue(
                updated.Contains("    private int ComputeIntermediate", StringComparison.Ordinal),
                $"Synthesized method must be indented to class scope. Got:\n{updated}");
        }
        finally
        {
            await CleanupAsync(workspaceId, solutionDir);
        }
    }

    /// <summary>
    /// dr-9-9: closing braces must be separated by a newline (no `}}` glue) and the
    /// extracted body must keep statement indentation. Validates the verbatim shape
    /// from the Firewall-Analyzer FORMAT-BUG-004 reproduction.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_ClosingBracesAndBodyIndentation_AreNotMalformed()
    {
        var (workspaceId, fixturePath, solutionDir) = await CreateFixtureAsync(
            "Dr99BraceHost.cs",
            """
            namespace SampleLib;

            public class Dr99BraceHost
            {
                public int Add(int a, int b)
                {
                    var x = a;
                    var y = b;
                    var combined = x + y;
                    return combined;
                }
            }
            """);

        try
        {
            // Extract the three statements that compute `combined` (lines 7-9).
            // `combined` flows out (used in the return) so the call site is
            // `var combined = ComputeCombined(a, b);` and the extracted method has
            // a `return combined;` body line.
            var preview = await ExtractMethodService.PreviewExtractMethodAsync(
                workspaceId,
                fixturePath,
                startLine: 7, startColumn: 9,
                endLine: 9, endColumn: 31,
                methodName: "ComputeCombined",
                ct: CancellationToken.None);

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(apply.Success, $"Apply must succeed. Error: {apply.Error}");

            var updated = await File.ReadAllTextAsync(fixturePath);

            // 9.9 shape A: closing braces NOT glued. The class-closing `}` must be on its
            // own line, separated from the method-closing `}` by at least one newline.
            // The bug shape was a literal `}}` substring at any point in the file.
            Assert.IsFalse(
                updated.Contains("}}", StringComparison.Ordinal),
                $"Regression: closing braces glued together (`}}`}}). Full file:\n{updated}");

            // 9.9 shape B: body statements inside the synthesized method must be indented.
            // With class-scope indent of 4 spaces, body statements should sit at 8 spaces.
            // Look for the verbatim statement `var combined = x + y;` indented to 8 columns.
            Assert.IsTrue(
                updated.Contains("        var combined = x + y;", StringComparison.Ordinal),
                $"Body statements must keep editorconfig indentation (8 spaces here). Got:\n{updated}");

            // 9.9 shape C: a return statement appears inside the new method body, indented.
            Assert.IsTrue(
                updated.Contains("        return combined;", StringComparison.Ordinal),
                $"`return combined;` must be indented to body scope. Got:\n{updated}");

            // 9.9 shape D: there is no stray `-}` at the very end (the audit reported a
            // dangling `-}` at end-of-file when the diff was applied). Trim trailing
            // whitespace/newlines and ensure the file ends with a single `}`.
            var trimmed = updated.TrimEnd();
            Assert.IsTrue(
                trimmed.EndsWith("}", StringComparison.Ordinal),
                $"File must end cleanly with the class brace. Got tail: {trimmed[^Math.Min(20, trimmed.Length)..]}");
        }
        finally
        {
            await CleanupAsync(workspaceId, solutionDir);
        }
    }

    /// <summary>
    /// Compound shape: the preview must round-trip cleanly through Roslyn's whole-document
    /// formatter without changing any non-extracted content. If <c>format_check</c> would
    /// flag the resulting document, the fix has regressed back to the pre-fix shape.
    /// </summary>
    [TestMethod]
    public async Task ExtractMethod_OutputIsAlreadyClean_ForFormatVerify()
    {
        var (workspaceId, fixturePath, solutionDir) = await CreateFixtureAsync(
            "DrFormatVerifyHost.cs",
            """
            namespace SampleLib;

            public class DrFormatVerifyHost
            {
                public int Tally(int a, int b)
                {
                    var sum = a + b;
                    var doubled = sum * 2;
                    return doubled;
                }
            }
            """);

        try
        {
            // Extract the two `var` decls — `doubled` is the single flowsOut variable
            // (used in the return), so the call site is `var doubled = M(a, b);`.
            var preview = await ExtractMethodService.PreviewExtractMethodAsync(
                workspaceId,
                fixturePath,
                startLine: 7, startColumn: 9,
                endLine: 8, endColumn: 31,
                methodName: "BuildDoubled",
                ct: CancellationToken.None);

            var apply = await RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, CancellationToken.None);
            Assert.IsTrue(apply.Success, $"Apply must succeed. Error: {apply.Error}");

            var post = await File.ReadAllTextAsync(fixturePath);

            // The applied file must be byte-identical to a Roslyn-formatted version of itself.
            // If the formatter would change a single byte, the extract emitted code that
            // violates project formatting (the original 9.7 / 9.9 shape).
            var formatted = await FormatViaRoslynAsync(post);
            Assert.AreEqual(
                formatted,
                post,
                "Extract method output is not idempotent under Roslyn's Formatter — this is the dr-9-7 / dr-9-9 regression shape.");
        }
        finally
        {
            await CleanupAsync(workspaceId, solutionDir);
        }
    }

    private static async Task<(string WorkspaceId, string FixturePath, string SolutionDir)> CreateFixtureAsync(
        string fileName, string sourceText)
    {
        var copiedSolutionPath = CreateSampleSolutionCopy();
        var solutionDir = Path.GetDirectoryName(copiedSolutionPath)!;
        var sampleLibDir = Path.Combine(solutionDir, "SampleLib");
        var fixturePath = Path.Combine(sampleLibDir, fileName);
        await File.WriteAllTextAsync(fixturePath, sourceText);

        var loadResult = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        return (loadResult.WorkspaceId, fixturePath, solutionDir);
    }

    private static async Task CleanupAsync(string workspaceId, string solutionDir)
    {
        WorkspaceManager.Close(workspaceId);
        await Task.Yield();
        TryDeleteDirectory(solutionDir);
    }

    private static async Task<string> FormatViaRoslynAsync(string source)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var formatted = Microsoft.CodeAnalysis.Formatting.Formatter.Format(
            await tree.GetRootAsync().ConfigureAwait(false),
            workspace);
        return formatted.ToFullString();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
