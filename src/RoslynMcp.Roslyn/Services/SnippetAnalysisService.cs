using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace RoslynMcp.Roslyn.Services;

public sealed class SnippetAnalysisService : ISnippetAnalysisService
{
    private readonly ILogger<SnippetAnalysisService> _logger;

    private static readonly string[] DefaultUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Threading.Tasks"
    ];

    public SnippetAnalysisService(ILogger<SnippetAnalysisService> logger) => _logger = logger;

    public Task<SnippetAnalysisDto> AnalyzeAsync(string code, string[]? usings, string kind, CancellationToken ct)
    {
        var allUsings = DefaultUsings
            .Concat(usings ?? [])
            .Distinct()
            .ToList();

        var usingBlock = string.Join("\n", allUsings.Select(u => $"using {u};"));
        var (wrappedCode, userStartLine, userStartColumn) = WrapCode(code, kind, usingBlock, allUsings.Count);

        // UX-001/FLAG-C: Diagnostics from the wrapped source carry line/column positions relative
        // to the wrapper, not the user's input. Subtract userStartLine from the wrapped line, and
        // for diagnostics that land on the first wrapped user line also subtract the column prefix
        // length (the wrapper text inserted before the user code). Subsequent user lines start at
        // wrapped column 1 so they need no column transform.

        var tree = CSharpSyntaxTree.ParseText(wrappedCode, cancellationToken: ct);

        // Create a minimal compilation with core references
        var references = GetCoreReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "SnippetAnalysis_" + Guid.NewGuid().ToString("N")[..8],
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var diagnostics = compilation.GetDiagnostics(ct)
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .ToList();

        int errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        var diagnosticDtos = diagnostics.Select(d =>
        {
            var lineSpan = d.Location.GetMappedLineSpan();
            int? StartLine = null, StartCol = null, EndLine = null, EndCol = null;
            if (lineSpan.IsValid)
            {
                // Convert from wrapped (0-based) to user (1-based) coordinates.
                var wrappedStartLine = lineSpan.StartLinePosition.Line + 1;
                var wrappedStartCol = lineSpan.StartLinePosition.Character + 1;
                var wrappedEndLine = lineSpan.EndLinePosition.Line + 1;
                var wrappedEndCol = lineSpan.EndLinePosition.Character + 1;

                StartLine = Math.Max(1, wrappedStartLine - userStartLine + 1);
                StartCol = wrappedStartLine == userStartLine
                    ? Math.Max(1, wrappedStartCol - userStartColumn + 1)
                    : wrappedStartCol;

                EndLine = Math.Max(1, wrappedEndLine - userStartLine + 1);
                EndCol = wrappedEndLine == userStartLine
                    ? Math.Max(1, wrappedEndCol - userStartColumn + 1)
                    : wrappedEndCol;
            }

            return new DiagnosticDto(
                Id: d.Id,
                Message: d.GetMessage(),
                Severity: d.Severity.ToString(),
                Category: d.Descriptor.Category,
                FilePath: null,
                StartLine: StartLine,
                StartColumn: StartCol,
                EndLine: EndLine,
                EndColumn: EndCol);
        }).ToList();

        // Extract declared symbols from the compilation
        var declaredSymbols = new List<string>();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(ct);

        foreach (var decl in root.DescendantNodes().Where(n =>
            n is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax or
                 Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax or
                 Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax or
                 Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax))
        {
            var symbol = model.GetDeclaredSymbol(decl, ct);
            if (symbol is not null)
                declaredSymbols.Add($"{symbol.Kind}: {symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
        }

        return Task.FromResult(new SnippetAnalysisDto(
            IsValid: errorCount == 0,
            ErrorCount: errorCount,
            WarningCount: warningCount,
            Diagnostics: diagnosticDtos,
            DeclaredSymbols: declaredSymbols.Count > 0 ? declaredSymbols : null));
    }

    /// <summary>
    /// Wraps user snippet text in scaffolding appropriate for the requested kind, and returns the
    /// (1-based) wrapped line and column at which the user code begins. The user-coordinate
    /// translation in <see cref="AnalyzeAsync"/> uses these so diagnostics map back to the
    /// original snippet position rather than the wrapped emit position.
    /// </summary>
    private static (string Wrapped, int UserStartLine, int UserStartColumn) WrapCode(
        string code, string kind, string usingBlock, int usingsLineCount)
    {
        // The wrapper always emits N using lines, then a newline, then the wrap line on which the
        // user code starts. So userStartLine = N + 1 in 1-based wrapped coordinates.
        var userStartLine = usingsLineCount + 1;

        const string ExpressionPrefix = "public static class Snippet { public static object Evaluate() => ";
        const string StatementsPrefix = "public static class Snippet { public static void Run() { ";
        const string ReturnExprPrefix = "public static class Snippet { public static object? Run() { ";
        const string MembersPrefix = "public class Snippet { ";

        return kind.ToLowerInvariant() switch
        {
            "expression" =>
                ($"{usingBlock}\n{ExpressionPrefix}{code}; }}",
                 userStartLine, ExpressionPrefix.Length + 1),
            "statements" =>
                ($"{usingBlock}\n{StatementsPrefix}{code} }} }}",
                 userStartLine, StatementsPrefix.Length + 1),
            "returnexpression" =>
                ($"{usingBlock}\n{ReturnExprPrefix}{code} }} }}",
                 userStartLine, ReturnExprPrefix.Length + 1),
            "members" =>
                ($"{usingBlock}\n{MembersPrefix}{code} }}",
                 userStartLine, MembersPrefix.Length + 1),
            "program" or "" =>
                ($"{usingBlock}\n{code}", userStartLine, 1),
            _ => throw new ArgumentException(
                $"Invalid snippet kind '{kind}'. Must be 'expression', 'statements', 'returnExpression', 'members', or 'program'.")
        };
    }

    private static ImmutableArray<MetadataReference> GetCoreReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];

        // Include all System.* and Microsoft.* platform assemblies so that user-supplied
        // usings (e.g. System.Text.Json, System.Net.Http) resolve correctly.
        return trustedAssemblies
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith("Microsoft.Extensions.", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
