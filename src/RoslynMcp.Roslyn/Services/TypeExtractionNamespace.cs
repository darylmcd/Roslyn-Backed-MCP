using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>The destination's namespace and imports, with the original lexical scope boundaries.</summary>
internal sealed class TypeExtractionNamespace
{
    private readonly CompilationUnitSyntax _sourceRoot;
    private readonly TypeDeclarationSyntax _sourceType;
    private readonly IdentifierNameSyntax _typeName;
    private readonly UsingDirectiveSyntax[] _fileUsings;
    private readonly SyntaxTriviaList _fileSymbols;
    private readonly (BaseNamespaceDeclarationSyntax Declaration, Header Header)[] _scopes;
    private readonly OrphanDirectiveRewriter _orphanDirectives;

    internal TypeExtractionNamespace(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax sourceType, string newTypeName)
    {
        _sourceRoot = sourceRoot;
        _sourceType = sourceType;
        _typeName = SyntaxFactory.IdentifierName(IdentifierValidation.CreateMemberIdentifierToken(newTypeName));
        // Global usings still apply from the source document. Copying a global alias
        // would declare it twice in the same project.
        _fileUsings = sourceRoot.Usings.Where(import => !import.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).ToArray();
        _scopes = sourceType.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Select(scope => (scope, GetHeader(scope))).ToArray();

        var copiedNodes = sourceRoot.Externs.Cast<SyntaxNode>().Concat(_fileUsings)
            .Concat(_scopes.SelectMany(scope => scope.Declaration.Externs.Cast<SyntaxNode>().Concat(scope.Declaration.Usings)))
            .Concat(_scopes.Select(scope => scope.Declaration.Name)).ToArray();
        var headerTrivia = _scopes.SelectMany(scope => scope.Header.SelectedTrivia).ToArray();
        var fileSymbolDirectives = GetFileSymbolDirectives(sourceRoot, headerTrivia);
        _fileSymbols = SyntaxFactory.TriviaList(fileSymbolDirectives.Select(directive => directive.ParentTrivia));
        _orphanDirectives = GetOrphanedDirectives(copiedNodes, headerTrivia, fileSymbolDirectives);
    }

    internal NameSyntax GetQualifiedTypeName()
    {
        var namespaceName = NamespaceMemberClassifier.GetContainingNamespaceName(_sourceType);
        return SyntaxFactory.ParseName(namespaceName.Length == 0
            ? $"global::{_typeName}"
            : $"global::{namespaceName}.{_typeName}");
    }

    internal UsingDirectiveSyntax? CreateReferenceAlias(SemanticModel model)
    {
        if (!_scopes.Any(scope => scope.Header.IsConditional))
        {
            return null;
        }

        // Bind a relative type name in the namespace that actually contains the
        // companion. Replaying a namespace condition at a call site can pick a
        // different branch after a file-local #define or #undef in the header.
        var inactiveTokens = _sourceRoot.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            .SelectMany(trivia => SyntaxFactory.ParseTokens(trivia.ToFullString()));
        var identifiers = _sourceRoot.DescendantTokens().Concat(inactiveTokens)
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText).ToHashSet(StringComparer.Ordinal);
        const string aliasPrefix = "__ExtractedType";
        var alias = aliasPrefix;
        for (var suffix = 1;
             identifiers.Contains(alias) || model.LookupSymbols(_sourceType.SpanStart, name: alias).Length > 0;
             suffix++)
        {
            alias = aliasPrefix + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return SyntaxFactory.UsingDirective(_typeName).WithAlias(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias)))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    internal CompilationUnitSyntax ReplaceSourceType(TypeDeclarationSyntax updatedType, UsingDirectiveSyntax? alias)
    {
        if (alias is null)
        {
            return _sourceRoot.ReplaceNode(_sourceType, updatedType);
        }

        // Add the alias to the same lexical scope while replacing the exact type.
        // The namespace already includes the rewritten descendant in its callback.
        SyntaxNode[] edits = [_scopes[0].Declaration, _sourceType];
        return _sourceRoot.ReplaceNodes(edits,
            (_, rewritten) => rewritten is BaseNamespaceDeclarationSyntax ns
                ? ns.WithUsings(ns.Usings.Add(alias))
                : updatedType);
    }

    internal static ExpressionSyntax AsExpression(NameSyntax name) =>
        name is QualifiedNameSyntax dotted
            ? SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, AsExpression(dotted.Left), dotted.DotToken, dotted.Right)
            : name;

    private static Header GetHeader(BaseNamespaceDeclarationSyntax scope)
    {
        var delimiter = scope switch
        {
            NamespaceDeclarationSyntax block => block.OpenBraceToken,
            FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.SemicolonToken,
            _ => default,
        };
        var firstBodyToken = delimiter.GetNextToken(includeZeroWidth: true);
        var header = scope.NamespaceKeyword.LeadingTrivia.Concat(scope.NamespaceKeyword.TrailingTrivia)
            .Concat(scope.Name.DescendantTrivia()).Concat(delimiter.LeadingTrivia).Concat(delimiter.TrailingTrivia);
        // A header conditional can own the keyword, the optional name segments,
        // or the delimiter. Its closing directive can be attached to the body.
        var spans = Directives(header).OfType<IfDirectiveTriviaSyntax>()
            .Select(directive => directive.GetRelatedDirectives())
            .Where(group => group.Count > 1 && group[^1] is EndIfDirectiveTriviaSyntax end &&
                end.FullSpan.End <= firstBodyToken.SpanStart)
            .Select(ConditionalSpan).ToArray();
        var bodyPrefix = InGroups(firstBodyToken.LeadingTrivia);
        // The imports are already copied as nodes. If the first token belongs
        // to one, its leading trivia is emitted there without being moved.
        var delimitersBodyPrefix = firstBodyToken.Parent is ExternAliasDirectiveSyntax or UsingDirectiveSyntax
            ? []
            : bodyPrefix;

        return new Header(InGroups(scope.NamespaceKeyword.LeadingTrivia), InGroups(delimiter.LeadingTrivia),
            delimitersBodyPrefix, InGroups(header).Concat(bodyPrefix).ToArray(), spans.Length > 0);

        SyntaxTrivia[] InGroups(IEnumerable<SyntaxTrivia> trivia) =>
            trivia.Where(item => spans.Any(span => span.Contains(item.FullSpan))).ToArray();
    }

    internal CompilationUnitSyntax Wrap(TypeDeclarationSyntax type)
    {
        MemberDeclarationSyntax member = type;
        foreach (var (scope, header) in _scopes)
        {
            var keyword = scope.NamespaceKeyword.WithLeadingTrivia(_orphanDirectives.CopyTrivia(header.KeywordPrefix));
            var name = _orphanDirectives.CopyNode(scope.Name);
            SyntaxToken Delimiter(SyntaxToken token) => token
                .WithLeadingTrivia(_orphanDirectives.CopyTrivia(header.DelimiterPrefix))
                .WithTrailingTrivia(token.TrailingTrivia.AddRange(_orphanDirectives.CopyTrivia(header.BodyPrefix)));
            BaseNamespaceDeclarationSyntax shell = scope is FileScopedNamespaceDeclarationSyntax fileScoped
                ? SyntaxFactory.FileScopedNamespaceDeclaration(name).WithNamespaceKeyword(keyword)
                    .WithSemicolonToken(Delimiter(fileScoped.SemicolonToken))
                : SyntaxFactory.NamespaceDeclaration(name).WithNamespaceKeyword(keyword)
                    .WithOpenBraceToken(Delimiter(((NamespaceDeclarationSyntax)scope).OpenBraceToken));
            member = shell.WithExterns(_orphanDirectives.Copy(scope.Externs))
                .WithUsings(_orphanDirectives.Copy(scope.Usings))
                .WithMembers(SyntaxFactory.SingletonList(member));
        }

        var root = SyntaxFactory.CompilationUnit()
            .WithExterns(_orphanDirectives.Copy(_sourceRoot.Externs))
            .WithUsings(_orphanDirectives.Copy(_fileUsings))
            .WithMembers(SyntaxFactory.SingletonList(member));
        return root.WithLeadingTrivia(_fileSymbols.AddRange(root.GetLeadingTrivia()));
    }

    private static DirectiveTriviaSyntax[] GetFileSymbolDirectives(CompilationUnitSyntax sourceRoot, SyntaxTrivia[] headerTrivia)
    {
        if (!sourceRoot.ContainsDirectives)
        {
            return [];
        }

        var headerPositions = Directives(headerTrivia).Select(directive => directive.SpanStart).ToHashSet();
        var directives = Directives(sourceRoot.DescendantTrivia()).ToArray();
        var symbols = directives.Where(directive => directive is DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax)
            .Where(directive => !headerPositions.Contains(directive.SpanStart)).ToArray();
        if (symbols.Length == 0)
        {
            return [];
        }

        // Keep the conditions that select each file-local define/undef. Moving
        // only the active directives would freeze the current parse options.
        var controllingDirectives = directives.OfType<IfDirectiveTriviaSyntax>()
            .Select(directive => directive.GetRelatedDirectives())
            .Where(group => group.Count > 1 && group[^1] is EndIfDirectiveTriviaSyntax &&
                symbols.Any(symbol => group[0].SpanStart < symbol.SpanStart && symbol.SpanStart < group[^1].SpanStart))
            .SelectMany(group => group);
        return symbols.Concat(controllingDirectives).OrderBy(directive => directive.SpanStart).ToArray();
    }

    private static OrphanDirectiveRewriter GetOrphanedDirectives(
        SyntaxNode[] copiedNodes, SyntaxTrivia[] headerTrivia, DirectiveTriviaSyntax[] fileSymbolDirectives)
    {
        var retained = copiedNodes.Select(node => node.FullSpan)
            .Concat(headerTrivia.Select(trivia => trivia.FullSpan)).ToArray();
        var omitted = fileSymbolDirectives.Select(directive => directive.SpanStart).ToHashSet();
        var omittedBranches = fileSymbolDirectives.OfType<IfDirectiveTriviaSyntax>()
            .Select(directive => ConditionalSpan(directive.GetRelatedDirectives())).ToList();
        foreach (var directive in Directives(copiedNodes.SelectMany(node => node.DescendantTrivia()).Concat(headerTrivia)))
        {
            if (omitted.Contains(directive.SpanStart))
            {
                continue;
            }

            // An import can own an opening #if whose #endif belongs to an omitted sibling.
            // Retain complete groups, including namespace headers.
            var related = directive.GetRelatedDirectives();
            if (related.Count <= 1 || related.All(boundary => retained.Any(span => span.Contains(boundary.Span))))
            {
                continue;
            }

            omitted.UnionWith(related.Select(boundary => boundary.SpanStart));
            if (related[0] is IfDirectiveTriviaSyntax)
            {
                omittedBranches.Add(ConditionalSpan(related));
            }
        }

        return new OrphanDirectiveRewriter(omitted, omittedBranches);
    }

    private static IEnumerable<DirectiveTriviaSyntax> Directives(IEnumerable<SyntaxTrivia> trivia) =>
        trivia.Where(item => item.IsDirective).Select(item => item.GetStructure()).OfType<DirectiveTriviaSyntax>();

    private static TextSpan ConditionalSpan(IReadOnlyList<DirectiveTriviaSyntax> group) =>
        TextSpan.FromBounds(group[0].FullSpan.Start, group[^1].FullSpan.End);

    private sealed record Header(
        SyntaxTrivia[] KeywordPrefix, SyntaxTrivia[] DelimiterPrefix, SyntaxTrivia[] BodyPrefix,
        SyntaxTrivia[] SelectedTrivia, bool IsConditional);

    private sealed class OrphanDirectiveRewriter(HashSet<int> omitted, List<TextSpan> omittedBranches) : CSharpSyntaxRewriter
    {
        internal SyntaxList<T> Copy<T>(IEnumerable<T> directives) where T : SyntaxNode =>
            SyntaxFactory.List(directives.Select(CopyNode));

        internal T CopyNode<T>(T node) where T : SyntaxNode =>
            Visit(node) is T rewritten ? rewritten : throw new InvalidOperationException("Expected to retain the projected scope node.");

        internal SyntaxTriviaList CopyTrivia(IEnumerable<SyntaxTrivia> trivia) =>
            SyntaxFactory.TriviaList(trivia.Select(VisitTrivia).Where(item => item.RawKind != 0));

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) =>
            (trivia.IsDirective && omitted.Contains(trivia.SpanStart)) ||
            (trivia.IsKind(SyntaxKind.DisabledTextTrivia) && omittedBranches.Any(branch => branch.Contains(trivia.Span)))
                ? default
                : trivia;
    }
}
