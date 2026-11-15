using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>The destination's namespace and imports, with the original lexical scope boundaries.</summary>
internal sealed class TypeExtractionNamespace
{
    private readonly string _newTypeName;
    private readonly SyntaxList<ExternAliasDirectiveSyntax> _fileExterns;
    private readonly UsingDirectiveSyntax[] _fileUsings;
    private readonly SyntaxTriviaList _fileSymbols;
    private readonly (BaseNamespaceDeclarationSyntax Syntax, SyntaxTriviaList KeywordPrefix, NameSyntax Name)[] _scopes;
    private readonly OrphanDirectiveRewriter _orphanDirectives;

    internal TypeExtractionNamespace(CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax sourceType, string newTypeName)
    {
        _newTypeName = newTypeName;
        _fileExterns = sourceRoot.Externs;
        // Global usings still apply from the source document. Copying a global alias
        // would declare it twice in the same project.
        _fileUsings = sourceRoot.Usings.Where(import => !import.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).ToArray();

        var scopes = sourceType.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
            .Select(scope => (Syntax: scope, Header: GetConditionalHeaderTrivia(scope))).ToArray();
        var copiedNodes = _fileExterns.Cast<SyntaxNode>().Concat(_fileUsings)
            .Concat(scopes.SelectMany(scope => scope.Syntax.Externs.Cast<SyntaxNode>().Concat(scope.Syntax.Usings)))
            .Concat(scopes.Select(scope => scope.Syntax.Name)).ToArray();
        var fileSymbolDirectives = GetFileSymbolDirectives(sourceRoot);
        _fileSymbols = SyntaxFactory.TriviaList(fileSymbolDirectives.Select(directive => directive.ParentTrivia));
        var headerTrivia = scopes.SelectMany(scope => scope.Header.KeywordPrefix.Concat(scope.Header.NameSuffix)).ToArray();
        _orphanDirectives = GetOrphanedDirectives(copiedNodes, headerTrivia, fileSymbolDirectives);
        _scopes = scopes.Select(scope =>
        {
            var name = _orphanDirectives.CopyNode(scope.Syntax.Name);
            var prefix = _orphanDirectives.CopyTrivia(scope.Header.KeywordPrefix);
            var suffix = _orphanDirectives.CopyTrivia(scope.Header.NameSuffix);
            return (scope.Syntax, prefix, name.WithTrailingTrivia(name.GetTrailingTrivia().AddRange(suffix)));
        }).ToArray();
    }

    internal NameSyntax GetQualifiedTypeName()
    {
        NameSyntax? name = null;
        foreach (var (scope, prefix, scopeName) in _scopes.Reverse())
        {
            name = AppendName(name, PrepareQualifier(scopeName, scope.NamespaceKeyword, prefix));
        }

        return AppendName(name, SyntaxFactory.IdentifierName(IdentifierValidation.CreateMemberIdentifierToken(_newTypeName)));
    }

    internal static ExpressionSyntax AsExpression(NameSyntax name) =>
        name is QualifiedNameSyntax dotted
            ? SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, AsExpression(dotted.Left), dotted.DotToken, dotted.Right)
            : name;

    private static NameSyntax PrepareQualifier(NameSyntax name, SyntaxToken namespaceKeyword, SyntaxTriviaList keywordPrefix)
    {
        if (keywordPrefix.Count > 0)
        {
            // A namespace declaration can repeat its keyword in each branch.
            // A reference needs the same conditional names without those keywords.
            name = name.WithLeadingTrivia(keywordPrefix.AddRange(name.GetLeadingTrivia()));
            var inactiveBranches = name.DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia));
            name = name.ReplaceTrivia(inactiveBranches, static (original, _) => RemoveNamespaceKeywords(original));
        }

        if (!name.ContainsDirectives)
        {
            return name.WithoutTrivia();
        }

        // In a declaration, the namespace keyword supplies the preceding line break.
        // In an expression, the newly inserted dot or global alias has no line break.
        var leading = name.GetLeadingTrivia();
        if (!leading.Any(trivia => trivia.IsDirective))
        {
            return name;
        }

        var lineBreak = namespaceKeyword.TrailingTrivia.FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        if (lineBreak.RawKind == 0)
        {
            lineBreak = SyntaxFactory.EndOfLine(Environment.NewLine);
        }

        return name.WithLeadingTrivia(leading.Insert(0, lineBreak));
    }

    private static SyntaxTrivia RemoveNamespaceKeywords(SyntaxTrivia inactiveBranch)
    {
        var text = inactiveBranch.ToFullString();
        var keywords = SyntaxFactory.ParseTokens(text).Where(token => token.IsKind(SyntaxKind.NamespaceKeyword))
            .Select(token => token.Span).Reverse();
        foreach (var keyword in keywords)
        {
            text = text.Remove(keyword.Start, keyword.Length);
        }

        return SyntaxFactory.DisabledText(text);
    }

    private static NameSyntax AppendName(NameSyntax? prefix, NameSyntax name) =>
        name is QualifiedNameSyntax dotted
            ? dotted.WithLeft(AppendName(prefix, dotted.Left))
            : prefix is null
                ? SyntaxFactory.AliasQualifiedName(
                    SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)), (SimpleNameSyntax)name)
                : SyntaxFactory.QualifiedName(prefix, (SimpleNameSyntax)name);

    private static (SyntaxTrivia[] KeywordPrefix, SyntaxTrivia[] NameSuffix) GetConditionalHeaderTrivia(BaseNamespaceDeclarationSyntax scope)
    {
        var delimiter = scope switch
        {
            NamespaceDeclarationSyntax block => block.OpenBraceToken,
            FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.SemicolonToken,
            _ => default,
        };
        var keywordTrivia = scope.NamespaceKeyword.LeadingTrivia;
        // A header conditional can start before the keyword or end at the
        // delimiter. Only retain groups that close within this header.
        var groups = keywordTrivia.Concat(scope.Name.DescendantTrivia()).Select(trivia => trivia.GetStructure())
            .OfType<IfDirectiveTriviaSyntax>()
            .Select(directive => directive.GetRelatedDirectives())
            .Where(group => group.Count > 1 && group[^1] is EndIfDirectiveTriviaSyntax end &&
                end.FullSpan.End <= delimiter.SpanStart).ToArray();
        var openingPositions = groups.Select(group => group[0].SpanStart).ToHashSet();
        var closingPositions = groups.Select(group => group[^1].SpanStart).ToHashSet();
        var firstOpeningDirective = keywordTrivia.FirstOrDefault(trivia =>
            trivia.IsKind(SyntaxKind.IfDirectiveTrivia) && openingPositions.Contains(trivia.SpanStart));
        var lastClosingDirective = delimiter.LeadingTrivia.LastOrDefault(trivia =>
            trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia) && closingPositions.Contains(trivia.SpanStart));
        return (
            firstOpeningDirective.RawKind == 0
                ? []
                : keywordTrivia.SkipWhile(trivia => trivia.SpanStart < firstOpeningDirective.SpanStart).ToArray(),
            lastClosingDirective.RawKind == 0
                ? []
                : delimiter.LeadingTrivia.TakeWhile(trivia => trivia.SpanStart <= lastClosingDirective.SpanStart).ToArray());
    }

    internal CompilationUnitSyntax Wrap(TypeDeclarationSyntax type)
    {
        MemberDeclarationSyntax member = type;
        foreach (var (scope, prefix, name) in _scopes)
        {
            var keyword = scope.NamespaceKeyword.WithLeadingTrivia(prefix);
            BaseNamespaceDeclarationSyntax shell = scope is FileScopedNamespaceDeclarationSyntax
                ? SyntaxFactory.FileScopedNamespaceDeclaration(name).WithNamespaceKeyword(keyword)
                : SyntaxFactory.NamespaceDeclaration(name).WithNamespaceKeyword(keyword);
            member = shell.WithExterns(_orphanDirectives.Copy(scope.Externs))
                .WithUsings(_orphanDirectives.Copy(scope.Usings))
                .WithMembers(SyntaxFactory.SingletonList(member));
        }

        var root = SyntaxFactory.CompilationUnit()
            .WithExterns(_orphanDirectives.Copy(_fileExterns))
            .WithUsings(_orphanDirectives.Copy(_fileUsings))
            .WithMembers(SyntaxFactory.SingletonList(member));
        return root.WithLeadingTrivia(_fileSymbols.AddRange(root.GetLeadingTrivia()));
    }

    private static DirectiveTriviaSyntax[] GetFileSymbolDirectives(CompilationUnitSyntax sourceRoot)
    {
        if (!sourceRoot.ContainsDirectives)
        {
            return [];
        }

        var directives = sourceRoot.DescendantTrivia()
            .Where(trivia => trivia.IsDirective)
            .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>().ToArray();
        var symbols = directives.Where(directive => directive is DefineDirectiveTriviaSyntax or UndefDirectiveTriviaSyntax)
            .ToArray();
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
        foreach (var directive in copiedNodes.SelectMany(node => node.DescendantTrivia()).Concat(headerTrivia)
                     .Where(trivia => trivia.IsDirective)
                     .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>())
        {
            if (omitted.Contains(directive.SpanStart))
            {
                continue;
            }

            // An import can own an opening #if whose #endif belongs to an omitted sibling.
            // Retain complete groups, including conditionals inside a qualified namespace name.
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

    private static TextSpan ConditionalSpan(IReadOnlyList<DirectiveTriviaSyntax> group) =>
        TextSpan.FromBounds(group[0].FullSpan.Start, group[^1].FullSpan.End);

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
