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
            .Select(u => $"using {u};");

        var usingBlock = string.Join("\n", allUsings);
        var wrappedCode = WrapCode(code, kind, usingBlock);

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
            return new DiagnosticDto(
                Id: d.Id,
                Message: d.GetMessage(),
                Severity: d.Severity.ToString(),
                Category: d.Descriptor.Category,
                FilePath: null,
                StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
                StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
                EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
                EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null);
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

    private static string WrapCode(string code, string kind, string usingBlock) => kind.ToLowerInvariant() switch
    {
        "expression" => $"{usingBlock}\npublic static class Snippet {{ public static object Evaluate() => {code}; }}",
        "statements" => $"{usingBlock}\npublic static class Snippet {{ public static void Run() {{ {code} }} }}",
        "members" => $"{usingBlock}\npublic class Snippet {{ {code} }}",
        "program" or "" => $"{usingBlock}\n{code}",
        _ => throw new ArgumentException($"Invalid snippet kind '{kind}'. Must be 'expression', 'statements', 'members', or 'program'.")
    };

    private static ImmutableArray<MetadataReference> GetCoreReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];

        var coreAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Runtime",
            "System.Console",
            "System.Collections",
            "System.Collections.Generic",
            "System.Linq",
            "System.Threading.Tasks",
            "System.Text",
            "System.Private.CoreLib",
            "netstandard",
            "System.ObjectModel",
            "System.Runtime.Extensions"
        };

        return trustedAssemblies
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return coreAssemblies.Contains(name) ||
                       name.StartsWith("System.Runtime", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
