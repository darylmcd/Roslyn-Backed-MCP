using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
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

        var (membersToExtract, membersToKeep) = PartitionMembers(typeDecl, memberNames, sourceTypeName);

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

        // dr-9-1-does-not-update-external-consumer-call-sites (SampleSolution audit §9.1):
        // If any extracted member is referenced from a file OUTSIDE the source file, applying
        // the extraction silently breaks those callers — the methods move to the new type but
        // the new type is constructor-injected as a private field on the source, so external
        // code calling `source.ExtractedMember()` no longer compiles. Refuse the preview so the
        // caller knows to either pull the affected callers into the extraction redesign, or to
        // first refactor those callers to interact with the new type directly via DI / factory.
        var externalConsumerWarnings = await CollectExternalConsumerWarningsAsync(
            solution, sourceDocument, semanticModel, membersToExtract, ct).ConfigureAwait(false);
        if (externalConsumerWarnings.Count > 0)
        {
            var summary = string.Join("; ", externalConsumerWarnings);
            throw new InvalidOperationException(
                $"Refusing to extract type '{newTypeName}' from '{sourceTypeName}': the selected member(s) are " +
                $"referenced by external consumer files. Applying the extraction would move the members to the new " +
                $"type (constructor-injected as a private field on the source), breaking every external call site. " +
                $"Either include the calling code in the extraction redesign, or first refactor consumers to interact " +
                $"with the new type directly via DI / a public factory. Details: {summary}");
        }

        // Determine target file path
        var sourceDir = Path.GetDirectoryName(sourceDocument.FilePath!)!;
        var resolvedTargetPath = newFilePath ?? Path.Combine(sourceDir, $"{newTypeName}.cs");
        resolvedTargetPath = Path.GetFullPath(resolvedTargetPath);

        // Build the new type declaration with extracted members
        var newFileRoot = BuildNewFileRoot(sourceRoot, typeDecl, membersToExtract, newTypeName);

        // Remove extracted members from source type and inject field + ctor parameter
        var fieldName = "_" + char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
        var updatedTypeDecl = InjectFieldAndCtorParameter(
            typeDecl.WithMembers(SyntaxFactory.List(membersToKeep)),
            newTypeName, fieldName);

        // Replace in source root (normalize so field/modifier tokens get proper spacing)
        var updatedSourceRoot = sourceRoot.ReplaceNode(typeDecl, updatedTypeDecl);
        if (updatedSourceRoot is CompilationUnitSyntax normalizedRoot)
            updatedSourceRoot = normalizedRoot.NormalizeWhitespace();

        var newSolution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, updatedSourceRoot);

        // Add new document. Item #1 — pass folders so MSBuildWorkspace's TryApplyChanges
        // resolves the disk path consistently with our explicit write.
        var targetFileName = Path.GetFileName(resolvedTargetPath);
        var targetProject = newSolution.GetProject(sourceDocument.Project.Id)!;
        var folders = ProjectMetadataParser.ComputeDocumentFolders(targetProject.FilePath, resolvedTargetPath);
        var newDoc = targetProject.AddDocument(targetFileName, newFileRoot.ToFullString(), folders: folders, filePath: resolvedTargetPath);
        newSolution = newDoc.Project.Solution;

        // Compute diff
        var changes = await SolutionDiffHelper.ComputeChangesAsync(solution, newSolution, ct).ConfigureAwait(false);
        var description = $"Extract {membersToExtract.Count} member(s) from '{sourceTypeName}' into new type '{newTypeName}'";
        var token = _previewStore.Store(workspaceId, newSolution, _workspace.GetCurrentVersion(workspaceId), description, changes);

        return new RefactoringPreviewDto(token, description, changes, warnings.Count > 0 ? warnings : null);
    }

    private static (List<MemberDeclarationSyntax> ToExtract, List<MemberDeclarationSyntax> ToKeep) PartitionMembers(
        TypeDeclarationSyntax typeDecl, IReadOnlyList<string> memberNames, string sourceTypeName)
    {
        var memberNameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        var toExtract = new List<MemberDeclarationSyntax>();
        var toKeep = new List<MemberDeclarationSyntax>();

        foreach (var member in typeDecl.Members)
        {
            var name = GetMemberName(member);
            if (name is not null && memberNameSet.Contains(name))
            {
                toExtract.Add(member);
                memberNameSet.Remove(name);
            }
            else
            {
                toKeep.Add(member);
            }
        }

        if (memberNameSet.Count > 0)
            throw new InvalidOperationException(
                $"Members not found in type '{sourceTypeName}': {string.Join(", ", memberNameSet)}");

        if (toExtract.Count == 0)
            throw new InvalidOperationException("No members matched for extraction.");

        return (toExtract, toKeep);
    }

    private static CompilationUnitSyntax BuildNewFileRoot(
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax typeDecl,
        IReadOnlyList<MemberDeclarationSyntax> membersToExtract,
        string newTypeName)
    {
        // The new type is emitted as `public sealed class NewType` with NO base list, so any
        // inheritance-only modifiers on the extracted members (`override`, `virtual`, `abstract`,
        // `sealed`, `new`) become compile errors or meaningless noise. Strip them alongside the
        // access-modifier normalization. Tracked by
        // `dr-9-3-preserves-when-new-type-does-not-inherit-the-bas`.
        var extractedMembers = membersToExtract
            .Select(EnsurePublicAccessibility)
            .Select(StripInheritanceOnlyModifiers)
            .ToList();
        TypeDeclarationSyntax newTypeDecl = SyntaxFactory.ClassDeclaration(newTypeName)
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithMembers(SyntaxFactory.List(extractedMembers));

        var namespaceDecl = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        MemberDeclarationSyntax topLevelMember = namespaceDecl switch
        {
            FileScopedNamespaceDeclarationSyntax fileScopedNs =>
                SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl)),
            NamespaceDeclarationSyntax blockNs =>
                SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newTypeDecl)),
            _ => newTypeDecl
        };

        return SyntaxFactory.CompilationUnit()
            .WithUsings(sourceRoot.Usings)
            .WithMembers(SyntaxFactory.SingletonList(topLevelMember))
            .NormalizeWhitespace();
    }

    private static TypeDeclarationSyntax InjectFieldAndCtorParameter(
        TypeDeclarationSyntax typeDecl, string newTypeName, string fieldName)
    {
        var fieldDecl = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(newTypeName))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(fieldName))))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        var existingCtor = typeDecl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (existingCtor is not null)
        {
            var paramName = char.ToLowerInvariant(newTypeName[0]) + newTypeName[1..];
            var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(newTypeName));

            // BUG-005 (#1): Insert the new required parameter BEFORE any optional parameters.
            // AddParameterListParameters appends to the end, which produced CS1737
            // ("required after optional") whenever the existing constructor ended with an
            // ILogger? logger = null or similar default.
            var existingParams = existingCtor.ParameterList.Parameters;
            var firstOptionalIndex = existingParams.IndexOf(p => p.Default is not null);
            ConstructorDeclarationSyntax updatedCtor = firstOptionalIndex < 0
                ? existingCtor.AddParameterListParameters(newParam)
                : existingCtor.WithParameterList(
                    existingCtor.ParameterList.WithParameters(
                        existingParams.Insert(firstOptionalIndex, newParam)));

            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    SyntaxFactory.IdentifierName(paramName)));

            if (updatedCtor.Body is not null)
                updatedCtor = updatedCtor.WithBody(updatedCtor.Body.AddStatements(assignment));

            typeDecl = typeDecl.ReplaceNode(existingCtor, updatedCtor);
        }

        return typeDecl.WithMembers(typeDecl.Members.Insert(0, fieldDecl));
    }

    /// <summary>
    /// Warns when extracted members reference symbols that remain on the original type (not moved with the extraction).
    /// </summary>
    /// <summary>
    /// dr-9-1-does-not-update-external-consumer-call-sites: For each member to be extracted,
    /// run a solution-wide reference search and collect any reference whose source-file path
    /// differs from the source document. Each external caller becomes a warning so the
    /// preview can refuse with an actionable message instead of silently producing a diff
    /// that the apply will break.
    /// </summary>
    private static async Task<List<string>> CollectExternalConsumerWarningsAsync(
        Solution solution,
        Document sourceDocument,
        SemanticModel semanticModel,
        IReadOnlyList<MemberDeclarationSyntax> membersToExtract,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sourceFilePath = sourceDocument.FilePath;

        foreach (var member in membersToExtract)
        {
            ct.ThrowIfCancellationRequested();
            var memberSymbol = semanticModel.GetDeclaredSymbol(member, ct);
            if (memberSymbol is null) continue;

            // Only public / internal members can be referenced from outside the source type
            // (private/protected references are confined to the source file or its derivatives,
            // which the existing dangling-reference check already handles).
            if (memberSymbol.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                continue;

            var references = await SymbolFinder.FindReferencesAsync(memberSymbol, solution, ct).ConfigureAwait(false);
            foreach (var refResult in references)
            {
                foreach (var loc in refResult.Locations)
                {
                    if (!loc.Location.IsInSource) continue;
                    var refDoc = solution.GetDocument(loc.Document.Id);
                    if (refDoc?.FilePath is null) continue;
                    if (string.Equals(refDoc.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var msg =
                        $"Extracted member '{memberSymbol.Name}' is referenced from external consumer " +
                        $"'{Path.GetFileName(refDoc.FilePath)}' (project '{refDoc.Project.Name}')";
                    if (seen.Add(msg))
                        warnings.Add(msg);
                }
            }
        }

        return warnings;
    }

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

    /// <summary>
    /// Strips modifiers that only make sense in the context of a base class or hidden member
    /// (<c>override</c>, <c>virtual</c>, <c>abstract</c>, <c>sealed</c>, <c>new</c>) from an
    /// extracted member. The new type has no base list, so these modifiers either fail to
    /// compile (CS0115 on <c>override</c>, CS0549 on <c>virtual</c>/<c>abstract</c> inside a
    /// sealed class) or silently hide nothing (<c>new</c>). Called after
    /// <see cref="EnsurePublicAccessibility"/> so both transforms compose.
    /// </summary>
    private static MemberDeclarationSyntax StripInheritanceOnlyModifiers(MemberDeclarationSyntax member)
    {
        var inheritanceOnly = new[]
        {
            SyntaxKind.OverrideKeyword,
            SyntaxKind.VirtualKeyword,
            SyntaxKind.AbstractKeyword,
            SyntaxKind.SealedKeyword,
            SyntaxKind.NewKeyword,
        };

        var currentModifiers = member switch
        {
            MethodDeclarationSyntax m => m.Modifiers,
            PropertyDeclarationSyntax p => p.Modifiers,
            FieldDeclarationSyntax f => f.Modifiers,
            EventDeclarationSyntax e => e.Modifiers,
            _ => default
        };

        if (currentModifiers.Count == 0)
            return member;

        var kept = currentModifiers.Where(tok => !inheritanceOnly.Contains(tok.Kind())).ToArray();
        if (kept.Length == currentModifiers.Count)
            return member;

        // Preserve the leading trivia from the original first modifier so the declaration
        // does not collapse against the preceding newline when an override-only modifier sat
        // at the front of the list.
        var leadingTrivia = currentModifiers[0].LeadingTrivia;
        if (kept.Length > 0)
            kept[0] = kept[0].WithLeadingTrivia(leadingTrivia);

        var tokenList = SyntaxFactory.TokenList(kept);

        // For members whose method body remains valid after dropping `abstract`, we need to
        // also ensure the declaration has a body (abstract members carry `;` instead). Roslyn
        // keeps a null body as an abstract-method shape; if we stripped `abstract` from a
        // method without a body we leave an invalid declaration. This cannot occur in the
        // current extract path (the source method already had a body to be extracted), but we
        // guard defensively so any future caller shape surfaces an explicit error instead of
        // silently emitting broken syntax.
        if (member is MethodDeclarationSyntax method
            && currentModifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))
            && method.Body is null
            && method.ExpressionBody is null)
        {
            throw new InvalidOperationException(
                $"Cannot extract abstract member '{method.Identifier.Text}' into a non-inheriting type: the source has no body.");
        }

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
