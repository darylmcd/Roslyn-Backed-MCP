using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed class ClassSplitOrchestrator : IClassSplitOrchestrator
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompositePreviewStore _compositePreviewStore;

    public ClassSplitOrchestrator(IWorkspaceManager workspace, ICompositePreviewStore compositePreviewStore)
    {
        _workspace = workspace;
        _compositePreviewStore = compositePreviewStore;
    }

    public async Task<RefactoringPreviewDto> PreviewSplitClassAsync(
        string workspaceId,
        string filePath,
        string typeName,
        IReadOnlyList<string> memberNames,
        string newFileName,
        CancellationToken ct)
    {
        if (memberNames.Count == 0)
        {
            throw new InvalidOperationException("Provide at least one member name to move into the partial class file.");
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var document = SymbolResolver.FindDocument(solution, filePath)
            ?? throw new InvalidOperationException($"Document not found: {filePath}");
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false) as CompilationUnitSyntax
            ?? throw new InvalidOperationException("Source document must be a C# compilation unit.");
        var typeDeclaration = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(candidate => string.Equals(candidate.Identifier.ValueText, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Type '{typeName}' was not found in '{filePath}'.");
        var description = $"Split class '{typeName}' into partial file '{newFileName}'";
        var containingTypes = typeDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().ToArray();
        if (typeDeclaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>()
            .Any(type => type.Modifiers.Any(SyntaxKind.FileKeyword)))
        {
            return new RefactoringPreviewDto(string.Empty, description, [], null,
                GuidanceMessage: "A file-local type or a type with a file-local ancestor cannot be split: its identity belongs to one physical file.");
        }

        var selectedMembers = typeDeclaration.Members
            .Where(member => GetMemberName(member) is string name && memberNames.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (selectedMembers.Length != memberNames.Count)
        {
            var foundNames = selectedMembers.Select(GetMemberName).Where(name => name is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
            var missingNames = memberNames.Where(name => !foundNames.Contains(name)).ToArray();
            throw new InvalidOperationException($"Member(s) not found in '{typeName}': {string.Join(", ", missingNames)}");
        }

        // split-class-preview-orphan-indent:
        // SyntaxRemoveOptions.KeepExteriorTrivia preserves the leading whitespace of removed
        // members as orphan trivia, leaving lines that contain only indentation (e.g. four
        // spaces followed by a newline) where the members used to live. Strip those orphans
        // via TriviaNormalizationHelper.RemoveOrphanIndentTrivia, which targets only
        // whitespace sandwiched between two end-of-line trivia tokens. Real indentation that
        // immediately precedes a token (i.e. inside preserved members) is never touched.
        var partialAfterRemoval = typeDeclaration.RemoveNodes(selectedMembers, SyntaxRemoveOptions.KeepExteriorTrivia)
            ?? throw new InvalidOperationException("Failed to remove the selected members from the original type.");
        var partialOriginal = EnsurePartial(TriviaNormalizationHelper.RemoveOrphanIndentTrivia(partialAfterRemoval));
        TypeDeclarationSyntax originalChild = typeDeclaration;
        var rewrittenChild = partialOriginal;
        foreach (var containingType in containingTypes)
        {
            rewrittenChild = EnsurePartial(containingType.ReplaceNode(originalChild, rewrittenChild));
            originalChild = containingType;
        }
        var updatedRoot = root.ReplaceNode(originalChild, rewrittenChild);

        // FORMAT-BUG-006 (dr-9-11-format-bug-006-duplicates-leading-trivia-into-b):
        // The original type declaration's leading trivia (XML doc comments, license headers,
        // explanatory single-line comments) and attribute lists must NOT be duplicated onto
        // the second partial. They stay with the first partial; the new partial gets only
        // minimal leading trivia (a leading newline so the partial sits below the namespace
        // header) and no attribute lists. Attributes on a partial class are merged by the
        // compiler, so leaving them on the original is sufficient.
        var partialNewType = CreatePartialPart(typeDeclaration, SyntaxFactory.List(selectedMembers));
        foreach (var containingType in containingTypes)
        {
            partialNewType = CreatePartialPart(containingType, SyntaxFactory.SingletonList<MemberDeclarationSyntax>(partialNewType))
                .WithoutLeadingTrivia().WithoutTrailingTrivia();
        }
        // New parts need real separators and braces after primary-constructor/semicolon
        // headers are removed. Normalize only generated text, never the original tree.
        partialNewType = partialNewType.NormalizeWhitespace()
            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        if (!partialNewType.GetTrailingTrivia().LastOrDefault().IsKind(SyntaxKind.EndOfLineTrivia))
        {
            partialNewType = partialNewType.WithTrailingTrivia(
                partialNewType.GetTrailingTrivia().Add(SyntaxFactory.CarriageReturnLineFeed));
        }
        var namespaceName = NamespaceMemberClassifier.GetContainingNamespaceName(typeDeclaration);
        var receivingOptions = ReceivingNamespaceFactory.RequireCSharpOptions(document.Project.ParseOptions);
        var partialCompilationUnit = CreateCompilationUnit(root, partialNewType, namespaceName, receivingOptions);
        var newFilePath = Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
        // dependency-inversion-noisy-diff: preserve trivia in the original tree; only format the new partial's type subtree if needed.
        var newFileContent = partialCompilationUnit.ToFullString();

        var originalContent = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var updatedOriginalContent = updatedRoot.ToFullString();
        var mutations = new List<CompositeFileMutation>
        {
            new(filePath, updatedOriginalContent, CompositeFileMutationMode.Edit),
            new(newFilePath, newFileContent, CompositeFileMutationMode.Create)
        };
        var changes = new List<FileChangeDto>
        {
            new(filePath, DiffGenerator.GenerateUnifiedDiff(originalContent, updatedOriginalContent, filePath)),
            new(newFilePath, DiffGenerator.GenerateUnifiedDiff(string.Empty, newFileContent, newFilePath))
        };

        var token = _compositePreviewStore.Store(workspaceId, _workspace.GetCurrentVersion(workspaceId), description, mutations);
        return new RefactoringPreviewDto(token, description, changes, null);
    }

    private static string? GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            _ => null
        };
    }

    private static TypeDeclarationSyntax EnsurePartial(TypeDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return declaration;

        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.ElasticSpace);
        return declaration.WithModifiers(declaration.Modifiers.Add(partialToken));
    }

    private static TypeDeclarationSyntax CreatePartialPart(TypeDeclarationSyntax declaration, SyntaxList<MemberDeclarationSyntax> members)
    {
        var typeParameters = declaration.TypeParameterList;
        if (typeParameters is not null)
        {
            typeParameters = typeParameters.WithParameters(SyntaxFactory.SeparatedList(
                typeParameters.Parameters.Select(parameter => parameter.WithAttributeLists(default))));
        }
        return EnsurePartial(declaration
            .WithAttributeLists(default)
            .WithBaseList(null)
            .WithParameterList(null)
            .WithTypeParameterList(typeParameters)
            .WithMembers(members)
            .WithOpenBraceToken(declaration.OpenBraceToken.IsKind(SyntaxKind.OpenBraceToken)
                ? declaration.OpenBraceToken : SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(declaration.CloseBraceToken.IsKind(SyntaxKind.CloseBraceToken)
                ? declaration.CloseBraceToken : SyntaxFactory.Token(SyntaxKind.CloseBraceToken))
            .WithSemicolonToken(default)
            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed));
    }

    private static CompilationUnitSyntax CreateCompilationUnit(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax declaration,
        string namespaceName, CSharpParseOptions receivingOptions)
    {
        // FORMAT-BUG-006 (dr-9-11-format-bug-006-duplicates-leading-trivia-into-b):
        // The first `using` directive in the source carries the file-level license header in
        // its leading trivia. Copying `sourceRoot.Usings` verbatim duplicates that header
        // into the new partial. Strip the leading trivia from the first using only.
        var copiedUsings = TriviaNormalizationHelper.StripLeadingTriviaFromFirstUsing(sourceRoot.Usings);
        var compilationUnit = SyntaxFactory.CompilationUnit().WithUsings(copiedUsings);
        var nsDecl = ReceivingNamespaceFactory.Create(namespaceName, receivingOptions);
        if (nsDecl is null)
        {
            return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        }

        nsDecl = nsDecl
            .WithNamespaceKeyword(
                SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(declaration));
        if (nsDecl is NamespaceDeclarationSyntax blockNamespace)
        {
            var closingBrace = blockNamespace.CloseBraceToken;
            if (!declaration.GetTrailingTrivia().LastOrDefault().IsKind(SyntaxKind.EndOfLineTrivia))
                closingBrace = closingBrace.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            nsDecl = blockNamespace
                .WithOpenBraceToken(blockNamespace.OpenBraceToken
                    .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)
                    .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed))
                .WithCloseBraceToken(closingBrace);
        }
        return compilationUnit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(nsDecl));
    }
}
