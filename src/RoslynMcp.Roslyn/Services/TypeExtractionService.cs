using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class TypeExtractionService : ITypeExtractionService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IPreviewStore _previewStore;
    private readonly ILogger<TypeExtractionService> _logger;

    public TypeExtractionService(IWorkspaceManager workspace, IPreviewStore previewStore, ILogger<TypeExtractionService> logger)
    {
        _workspace = workspace;
        _previewStore = previewStore;
        _logger = logger;
    }

    public async Task<RefactoringPreviewDto> PreviewExtractTypeAsync(
        string workspaceId, string filePath, string sourceTypeName,
        IReadOnlyList<string> memberNames, string newTypeName, string? newFilePath,
        CancellationToken ct)
    {
        if (memberNames.Count == 0)
            throw new ArgumentException("At least one member name must be specified.", nameof(memberNames));

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var sourceDocument = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");

        var sourceRoot = await sourceDocument.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");

        var semanticModel = await sourceDocument.GetSemanticModelAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Semantic model could not be created.");

        var typeDecl = sourceRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, sourceTypeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{sourceTypeName}' not found in {filePath}.");

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve type '{sourceTypeName}'.");

        // Find the member declarations to extract
        var memberNameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        var membersToExtract = new List<MemberDeclarationSyntax>();
        var membersToKeep = new List<MemberDeclarationSyntax>();

        foreach (var member in typeDecl.Members)
        {
            var memberName = GetMemberName(member);
            if (memberName is not null && memberNameSet.Contains(memberName))
            {
                membersToExtract.Add(member);
                memberNameSet.Remove(memberName);
            }
            else
            {
                membersToKeep.Add(member);
            }
        }

        if (memberNameSet.Count > 0)
        {
            throw new InvalidOperationException(
                $"Members not found in type '{sourceTypeName}': {string.Join(", ", memberNameSet)}");
        }

        if (membersToExtract.Count == 0)
            throw new InvalidOperationException("No members matched for extraction.");

        var warnings = CollectExtractTypeDanglingReferenceWarnings(semanticModel, typeSymbol, membersToExtract, ct);

        // BUG-005 (#2/#3): Refuse to generate code that the warnings prove will not compile.
        // The previous behavior emitted the warnings but still produced a preview that referenced
        // members staying on the source type, leading to broken builds when applied. Halting here
        // forces the caller to either include the missing members in the extraction or to redesign
        // the split before attempting it.
        if (warnings.Count > 0)
        {
            var summary = string.Join("; ", warnings);
            throw new InvalidOperationException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the selected members reference state " +
                $"that would remain on the source type, so the generated code would not compile. " +
                $"Either include the referenced members in the extraction or perform a manual redesign first. " +
                $"Details: {summary}");
        }

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var resolvedTargetPath = newFilePath ?? Path.Combine(sourceDir, $"{newTypeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Build the new type declaration with extracted members
        // Strip accessibility modifiers from extracted members (make them public in the new type)
        var extractedMembers = membersToExtract.Select(m => EnsurePublicAccessibility(m)).ToList();

        TypeDeclarationSyntax newTypeDecl = SyntaxFactory.ClassDeclaration(newTypeName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithMembers(SyntaxFactory.List(extractedMembers));

        // Build new file
        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        CompilationUnitSyntax newFileRoot;

        if (namespaceDecl is FileScopedNamespaceDeclarationSyntax fileScopedNs)
        {
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl));
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else if (namespaceDecl is NamespaceDeclarationSyntax blockNs)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl));
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs))
                .NormalizeWhitespace();
        }
        else
        {
            newFileRoot = SyntaxFactory.CompilationUnit()
                .WithUsings(sourceRoot.Usings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl))
                .NormalizeWhitespace();
        }

        // Remove extracted members from source type
        var updatedTypeDecl = typeDecl.WithMembers(SyntaxFactory.List(membersToKeep));

        // Add field and constructor parameter for the new type
        var fieldName = "_" + char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
        var fieldDecl = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(newTypeName))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        // Find existing constructor and add parameter, or create one
        var existingCtor = updatedTypeDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (existingCtor is not null)
        {
            var paramName = char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(newTypeName));

            // BUG-005 (#1): Insert the new required parameter BEFORE any optional parameters.
            // AddParameterListParameters appends to the end, which produced CS1737
            // ("required after optional") whenever the existing constructor ended with an
            // ILogger? logger = null or similar default. Find the first optional parameter and
            // splice the new one in just before it; if there are no optionals, append.
            var existingParams = existingCtor.ParameterList.Parameters;
            var firstOptionalIndex = -1;
            for (int i = 0; i < existingParams.Count; i++)
            {
                if (existingParams[i].Default is not null)
                {
                    firstOptionalIndex = i;
                    break;
                }
            }

            ConstructorDeclarationSyntax updatedCtor;
            if (firstOptionalIndex < 0)
            {
                updatedCtor = existingCtor.AddParameterListParameters(newParam);
            }
            else
            {
                var newParameterList = existingCtor.ParameterList.WithParameters(
                    existingParams.Insert(firstOptionalIndex, newParam));
                updatedCtor = existingCtor.WithParameterList(newParameterList);
            }

            // Add assignment to constructor body
            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    SyntaxFactory.IdentifierName(paramName)));

            if (updatedCtor.Body is not null)
            {
                updatedCtor = updatedCtor.WithBody(updatedCtor.Body.AddStatements(assignment));
            }

            updatedTypeDecl = updatedTypeDecl.ReplaceNode(existingCtor, updatedCtor);
        }

        // Insert field at the top of the type
        updatedTypeDecl = updatedTypeDecl.WithMembers(
            updatedTypeDecl.Members.Insert(0, fieldDecl));

        // Replace in source root (normalize so field/modifier tokens get proper spacing)
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);
        if (updatedSourceRoot is CompilationUnitSyntax normalizedRoot)
            updatedSourceRoot = normalizedRoot.NormalizeWhitespace();

        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add new document
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var newDoc = newSolution.GetProject(sourceDocument.Project.Id)!
            .AddDocument(targetFileName, newFileRoot.ToFullString(), filePath: resolvedTargetPath);
        newSolution = newDoc.Project.Solution;

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {membersToExtract.Count} member(s) from '{sourceTypeName}' into new type '{newTypeName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description);

        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// Warns when extracted members reference symbols that remain on the original type (not moved with the extraction).
    /// </summary>
    private static List<string> CollectExtractTypeDanglingReferenceWarnings(
        SemanticModel semanticModel,
        INamedTypeSymbol typeSymbol,
        IReadOnlyList<MemberDeclarationSyntax> membersToExtract,
        CancellationToken ct)
    {
        var extractedDeclared = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var m in membersToExtract)
        {
            var sym = semanticModel.GetDeclaredSymbol(m, ct);
            if (sym is not null)
                extractedDeclared.Add(sym);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var member in membersToExtract)
        {
            foreach (var node in member.DescendantNodesAndSelf())
            {
                ct.ThrowIfCancellationRequested();

                var sym = semanticModel.GetSymbolInfo(node, ct).Symbol;
                if (sym is null) continue;

                if (sym is ILocalSymbol or IParameterSymbol or ILabelSymbol)
                    continue;

                if (extractedDeclared.Contains(sym))
                    continue;

                if (!IsDeclaredInOrUnderType(sym, typeSymbol))
                    continue;

                if (sym is INamedTypeSymbol nt && SymbolEqualityComparer.Default.Equals(nt, typeSymbol))
                    continue;

                var msg =
                    $"Extracted member may reference '{sym.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' " +
                    $"which remains on the original type '{typeSymbol.Name}' and is not available in the new type.";
                if (seen.Add(msg))
                    warnings.Add(msg);
            }
        }

        return warnings;
    }

    private static bool IsDeclaredInOrUnderType(ISymbol sym, INamedTypeSymbol typeSymbol)
    {
        INamedTypeSymbol? t = sym.ContainingType;
        while (t is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(t, typeSymbol))
                return true;
            t = t.ContainingType;
        }

        return false;
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            _ => null
        };
    }

    private static MemberDeclarationSyntax EnsurePublicAccessibility(MemberDeclarationSyntax member)
    {
        // Remove existing access modifiers and add public
        var accessModifiers = new[]
        {
            SyntaxKind.PublicKeyword, SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword,
            SyntaxKind.InternalKeyword
        };

        var currentModifiers = member switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            _ => default
        };

        var newModifiers = currentModifiers
            .Where(m => !accessModifiers.Contains(m.Kind()))
            .Prepend(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        var tokenList = SyntaxFactory.TokenList(newModifiers);

        return member switch
        {
            MethodDeclarationSyntax m => m.WithModifiers(tokenList),
            PropertyDeclarationSyntax p => p.WithModifiers(tokenList),
            FieldDeclarationSyntax f => f.WithModifiers(tokenList),
            EventDeclarationSyntax e => e.WithModifiers(tokenList),
            _ => member
        };
    }
}
