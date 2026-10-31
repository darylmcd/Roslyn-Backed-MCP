using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Preserves physical file roles when an existing declaration moves into a new document.</summary>
internal static class MovedDeclarationFileWriter
{
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

    internal static CompilationUnitSyntax CreateCompilationUnit(
        CompilationUnitSyntax sourceRoot,
        MemberDeclarationSyntax member,
        SyntaxList<UsingDirectiveSyntax> copiedUsings,
        BaseNamespaceDeclarationSyntax? destinationNamespace,
        SemanticModel semanticModel,
        CancellationToken ct)
    {
        var prelude = GetFilePrelude(sourceRoot);
        var movedMember = StripPrelude(sourceRoot, member, prelude);
        var usings = SyntaxFactory.List(copiedUsings.Select(u => StripPrelude(sourceRoot, u, prelude)));
        var localUsings = GetNamespaceUsings(member, semanticModel, ct);
        var retainedSpans = copiedUsings.Select(u => u.FullSpan)
            .Concat(member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings).Select(u => u.FullSpan))
            .Append(member.FullSpan).Append(sourceRoot.EndOfFileToken.FullSpan).ToArray();
        var (before, after) = GetSurroundingDirectives(sourceRoot, member, retainedSpans, prelude);
        movedMember = movedMember.WithLeadingTrivia(before.AddRange(movedMember.GetLeadingTrivia()));
        movedMember = movedMember.WithTrailingTrivia(movedMember.GetTrailingTrivia().AddRange(after));

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
                .WithMembers(SyntaxFactory.SingletonList(movedMember));
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
        return root;
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
                var copied = import;
                if (import.Name is { } name &&
                    semanticModel.GetSymbolInfo(name, ct).Symbol is INamespaceOrTypeSymbol symbol)
                {
                    // Namespace-local imports bind relative to their old namespace. Make that
                    // binding explicit before placing them in the destination namespace.
                    copied = import.WithName(SyntaxFactory.ParseName(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .WithTriviaFrom(name));
                }
                result.Add(copied);
            }
        }
        return SyntaxFactory.List(result);
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
