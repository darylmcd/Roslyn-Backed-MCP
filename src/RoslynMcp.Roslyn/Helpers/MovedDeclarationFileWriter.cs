using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Preserves physical file roles when an existing declaration moves into a new document.</summary>
internal static class MovedDeclarationFileWriter
{
    internal static void EnsureTopLevelDeclaration(TypeDeclarationSyntax declaration)
    {
        if (!NamespaceMemberClassifier.IsDirectNamespaceMember(declaration))
        {
            throw new PublicInvalidOperationException(
                "Moving a nested type would change its containing type. Select a top-level type.");
        }
    }

    internal static BaseNamespaceDeclarationSyntax? FrameNamespace(
        BaseNamespaceDeclarationSyntax? destinationNamespace, SyntaxTriviaList bodySeparator) =>
        destinationNamespace switch
        {
            FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped
                .WithNamespaceKeyword(SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken).WithTrailingTrivia(bodySeparator)),
            NamespaceDeclarationSyntax block => block
                .WithNamespaceKeyword(SyntaxFactory.Token(SyntaxKind.NamespaceKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithTrailingTrivia(bodySeparator)),
            _ => destinationNamespace
        };

    internal static (CompilationUnitSyntax Root, TextSpan TypeIdentifier) CreateCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax member,
        SyntaxList<UsingDirectiveSyntax> copiedUsings,
        BaseNamespaceDeclarationSyntax? destinationNamespace,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var prelude = GetFilePrelude(sourceRoot);
        var usings = SyntaxFactory.List(copiedUsings.Select(u => StripPrelude(sourceRoot, u, prelude)));
        var (movedMember, localUsings) = PrepareMovedMember(
            sourceRoot, member, copiedUsings, prelude, endOfFileTravelsWithMember: true, semanticModel, ct);

        MemberDeclarationSyntax outputMember;
        if (destinationNamespace is null)
        {
            // Namespace aliases shadow file aliases. Moving them to the compilation unit must
            // retain the effective alias rather than emit two aliases with the same name.
            var localAliases = localUsings.Where(u => u.Alias is not null)
                .Select(u => u.Alias!.Name.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
            usings = SyntaxFactory.List(usings.Where(u => u.Alias is null ||
                !localAliases.Contains(u.Alias.Name.Identifier.ValueText)).Concat(localUsings));
            outputMember = movedMember;
        }
        else
        {
            outputMember = destinationNamespace.WithUsings(localUsings)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(movedMember));
        }

        var root = SyntaxFactory.CompilationUnit().WithUsings(usings)
            .WithMembers(SyntaxFactory.SingletonList(outputMember))
            .WithEndOfFileToken(sourceRoot.EndOfFileToken);
        // Set the prefix after the first real token exists; a prefix set on an empty factory
        // unit belongs to its EOF token and can disappear when members are subsequently added.
        root = root.WithLeadingTrivia(prelude.AddRange(root.GetLeadingTrivia()));
        if (root.Members.Single() is NamespaceDeclarationSyntax block &&
            !block.Members.Last().ToFullString().EndsWith('\n'))
        {
            // A final line comment must end before the new namespace's closing brace.
            root = root.ReplaceNode(block, block.WithCloseBraceToken(block.CloseBraceToken
                .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)));
        }
        var output = root.Members.Single();
        var type = (TypeDeclarationSyntax)(output is BaseNamespaceDeclarationSyntax ns ? ns.Members.Single() : output);
        return (root, type.Identifier.Span);
    }

    /// <summary>
    /// Builds the moved declaration and the namespace-scoped imports that must govern it at its
    /// destination: the declaration with every conditional / policy directive boundary it needs
    /// to stay balanced and conditional exactly as before, plus the enclosing namespace scopes'
    /// imports with their bindings made explicit. Shared by the new-file projection (where the end-of-file
    /// trivia is copied into the new file, so <paramref name="endOfFileTravelsWithMember"/> is true)
    /// and the same-file projection (where it stays in the retained file, so a directive pair that
    /// closes there must be duplicated around the moved copy).
    /// </summary>
    internal static (TypeDeclarationSyntax Member, SyntaxList<UsingDirectiveSyntax> NamespaceUsings) PrepareMovedMember(
        CompilationUnitSyntax sourceRoot,
        TypeDeclarationSyntax member,
        SyntaxList<UsingDirectiveSyntax> copiedUsings,
        SyntaxTriviaList prelude,
        bool endOfFileTravelsWithMember,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var movedMember = StripPrelude(sourceRoot, member, prelude);
        var localUsings = GetNamespaceUsings(member, semanticModel, ct);
        var retained = copiedUsings.Select(u => u.FullSpan)
            .Concat(member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings).Select(u => u.FullSpan))
            .Append(member.FullSpan);
        if (endOfFileTravelsWithMember)
            retained = retained.Append(sourceRoot.EndOfFileToken.FullSpan);
        var (before, after) = GetSurroundingDirectives(sourceRoot, member, retained.ToArray(), prelude);
        movedMember = movedMember.WithLeadingTrivia(before.AddRange(movedMember.GetLeadingTrivia()));
        movedMember = movedMember.WithTrailingTrivia(movedMember.GetTrailingTrivia().AddRange(after));
        return (movedMember, localUsings);
    }

    internal static CompilationUnitSyntax RemoveDeclaration(CompilationUnitSyntax sourceRoot, MemberDeclarationSyntax member)
    {
        var ownsPrelude = member.GetFirstToken() == sourceRoot.GetFirstToken();
        var prelude = GetFilePrelude(sourceRoot);
        var removalRoot = sourceRoot;
        var removalMember = member;
        if (ownsPrelude && prelude.Count > 0)
        {
            // Remove the physical prelude before retaining directives, then reattach it once
            // to the remaining file. Documentation continues to belong to the removed node.
            var annotation = new SyntaxAnnotation();
            removalRoot = sourceRoot.ReplaceNode(member, member
                .WithLeadingTrivia(member.GetLeadingTrivia().Skip(prelude.Count))
                .WithAdditionalAnnotations(annotation));
            removalMember = (MemberDeclarationSyntax)removalRoot.GetAnnotatedNodes(annotation).Single();
        }
        // Policy directives inside a removed declaration still govern following declarations.
        var remaining = removalRoot.RemoveNode(removalMember, SyntaxRemoveOptions.KeepDirectives)
            ?? throw InvalidOperationErrors.Internal("Failed to remove the moved declaration from its source document.");
        return ownsPrelude ? remaining.WithLeadingTrivia(prelude.AddRange(remaining.GetLeadingTrivia())) : remaining;
    }

    private static SyntaxTriviaList GetFilePrelude(CompilationUnitSyntax root) =>
        SyntaxFactory.TriviaList(root.GetLeadingTrivia().TakeWhile(t =>
            !t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
            !t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) &&
            t.GetStructure() is not (IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax or ElseDirectiveTriviaSyntax or
                EndIfDirectiveTriviaSyntax or RegionDirectiveTriviaSyntax or EndRegionDirectiveTriviaSyntax)));

    private static T StripPrelude<T>(CompilationUnitSyntax sourceRoot, T node, SyntaxTriviaList prelude)
        where T : SyntaxNode =>
        node.GetFirstToken() == sourceRoot.GetFirstToken()
            ? node.WithLeadingTrivia(node.GetLeadingTrivia().Skip(prelude.Count))
            : node;

    private static SyntaxList<UsingDirectiveSyntax> GetNamespaceUsings(
        MemberDeclarationSyntax member, SemanticModel semanticModel, CancellationToken ct)
    {
        var result = new List<UsingDirectiveSyntax>();
        foreach (var scope in member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse())
        {
            foreach (var import in scope.Usings)
            {
                if (import.Alias is { } alias)
                    result.RemoveAll(u => u.Alias?.Name.Identifier.ValueText == alias.Name.Identifier.ValueText);
                // Namespace-local imports bind relative to their old namespace. Make that
                // binding explicit before placing them in the destination namespace.
                result.Add(QualifyImport(import, semanticModel, ct, onlyWhenRelative: false));
            }
        }
        return SyntaxFactory.List(result);
    }

    /// <summary>
    /// Makes the namespace-scoped imports of a declaration whose namespace NAME is about to change
    /// keep their meaning: an import written relative to the old namespace (<c>using Sub;</c>
    /// meaning <c>Old.Sub</c>) would otherwise rebind to the new one. Only imports whose written
    /// name differs from the symbol they bind to are rewritten, so ordinary imports keep their text.
    /// </summary>
    internal static SyntaxList<UsingDirectiveSyntax> PreserveImportBindings(
        SyntaxList<UsingDirectiveSyntax> imports, SemanticModel semanticModel, CancellationToken ct) =>
        SyntaxFactory.List(imports.Select(import => QualifyImport(import, semanticModel, ct, onlyWhenRelative: true)));

    private static UsingDirectiveSyntax QualifyImport(
        UsingDirectiveSyntax import, SemanticModel semanticModel, CancellationToken ct, bool onlyWhenRelative)
    {
        if (import.Name is not { } name ||
            semanticModel.GetSymbolInfo(name, ct).Symbol is not INamespaceOrTypeSymbol symbol)
            return import;
        if (onlyWhenRelative &&
            string.Equals(name.WithoutTrivia().ToString(), symbol.ToDisplayString(), StringComparison.Ordinal))
            return import;
        return import.WithName(SyntaxFactory.ParseName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithTriviaFrom(name));
    }

    private static (SyntaxTriviaList Before, SyntaxTriviaList After) GetSurroundingDirectives(
        CompilationUnitSyntax root,
        MemberDeclarationSyntax member,
        Microsoft.CodeAnalysis.Text.TextSpan[] retainedSpans,
        SyntaxTriviaList prelude)
    {
        var directives = root.DescendantTrivia(descendIntoTrivia: true)
            .Select(t => t.GetStructure()).OfType<DirectiveTriviaSyntax>().ToArray();
        var missing = new Dictionary<int, DirectiveTriviaSyntax>();
        bool IsRetained(DirectiveTriviaSyntax d) =>
            retainedSpans.Any(span => span.Contains(d.FullSpan)) ||
            prelude.Any(t => t.FullSpan.Contains(d.FullSpan));

        foreach (var directive in directives)
        {
            var related = directive.GetRelatedDirectives();
            if (related.Count > 1 &&
                (related.Any(IsRetained) ||
                 related[0].SpanStart < member.SpanStart && related[^1].Span.End > member.Span.End))
            {
                foreach (var boundary in related.Where(d => !IsRetained(d)))
                    missing[boundary.SpanStart] = boundary;
            }
            else if (directive.SpanStart < member.SpanStart && !IsRetained(directive) &&
                directive is NullableDirectiveTriviaSyntax or PragmaWarningDirectiveTriviaSyntax)
            {
                missing[directive.SpanStart] = directive;
            }
        }

        var before = SyntaxFactory.TriviaList(missing.Values.Where(d => d.SpanStart < member.SpanStart)
            .OrderBy(d => d.SpanStart).Select(d => d.ParentTrivia));
        var after = SyntaxFactory.TriviaList(missing.Values.Where(d => d.SpanStart >= member.Span.End)
            .OrderBy(d => d.SpanStart).Select(d => d.ParentTrivia));
        if (after.Count > 0 && !member.ToFullString().EndsWith('\n'))
            after = after.Insert(0, SyntaxFactory.CarriageReturnLineFeed);
        return (before, after);
    }
}
