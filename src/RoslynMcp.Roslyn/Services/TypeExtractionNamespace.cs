using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;
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
        var scopeTrivia = _scopes.SelectMany(scope => scope.Header.SelectedTrivia).ToArray();
        var fileSymbolDirectives = GetFileSymbolDirectives(sourceRoot, scopeTrivia);
        _fileSymbols = SyntaxFactory.TriviaList(fileSymbolDirectives.Select(directive => directive.ParentTrivia));
        var inactiveSemicolons = _scopes.Where(scope => scope.Header.NormalizeToBlock)
            .SelectMany(scope => scope.Header.SelectedTrivia)
            .DistinctBy(trivia => trivia.SpanStart)
            .Select(trivia => (Trivia: trivia, Delimiter: InactiveDelimiter(trivia)))
            .Where(edit => edit.Delimiter.IsKind(SyntaxKind.SemicolonToken))
            .ToDictionary(edit => edit.Trivia.SpanStart, edit => edit.Delimiter.SpanStart);
        _orphanDirectives = GetOrphanedDirectives(copiedNodes, scopeTrivia, fileSymbolDirectives, inactiveSemicolons);
    }

    internal NameSyntax GetQualifiedTypeName()
    {
        var namespaceName = NamespaceMemberClassifier.GetContainingNamespaceName(_sourceType);
        return SyntaxFactory.ParseName(namespaceName.Length == 0
            ? $"global::{_typeName}"
            : $"global::{namespaceName}.{_typeName}");
    }

    internal UsingDirectiveSyntax? CreateReferenceAlias(SemanticModel model, CancellationToken ct)
    {
        if (!_scopes.Any(scope => scope.Header.IsConditional))
        {
            return null;
        }

        // Bind a relative type name in the namespace that actually contains the
        // companion. Replaying a namespace condition at a call site can pick a
        // different branch after a file-local #define or #undef in the header.
        const string aliasPrefix = "__ExtractedType";
        var identifiers = new HashSet<string>(StringComparer.Ordinal) { _typeName.Identifier.ValueText };
        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var root = tree.GetRoot(ct);
            var inactiveTokens = root.DescendantTrivia()
                .Where(trivia => trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                .SelectMany(trivia => SyntaxFactory.ParseTokens(trivia.ToFullString()));
            identifiers.UnionWith(root.DescendantTokens().Concat(inactiveTokens)
                .Where(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText.StartsWith(aliasPrefix, StringComparison.Ordinal))
                .Select(token => token.ValueText));
        }

        var alias = aliasPrefix;
        for (var suffix = 1;
             identifiers.Contains(alias) || model.LookupSymbols(_sourceType.OpenBraceToken.Span.End, name: alias).Length > 0 ||
                 CompilationHasMemberNamed(model.Compilation, alias, ct);
             suffix++)
        {
            alias = aliasPrefix + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return SyntaxFactory.UsingDirective(_typeName).WithAlias(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName(alias)))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static bool CompilationHasMemberNamed(Compilation compilation, string name, CancellationToken ct)
    {
        // A base type referenced only in another configuration can introduce
        // an inherited member with the alias's name.
        var scopes = new Stack<INamespaceOrTypeSymbol>();
        scopes.Push(compilation.Assembly.GlobalNamespace);
        foreach (var reference in compilation.References)
        {
            switch (compilation.GetAssemblyOrModuleSymbol(reference))
            {
                case IAssemblySymbol assembly:
                    scopes.Push(assembly.GlobalNamespace);
                    break;
                case IModuleSymbol module:
                    scopes.Push(module.GlobalNamespace);
                    break;
            }
        }

        var visited = new HashSet<INamespaceOrTypeSymbol>(SymbolEqualityComparer.Default);
        while (scopes.TryPop(out var scope))
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(scope))
            {
                continue;
            }

            if (scope.GetMembers(name).Any())
            {
                return true;
            }

            if (scope is INamespaceSymbol namespaceScope)
            {
                foreach (var child in namespaceScope.GetNamespaceMembers())
                    scopes.Push(child);
            }

            foreach (var child in scope.GetTypeMembers())
                scopes.Push(child);
        }

        return false;
    }

    internal CompilationUnitSyntax ReplaceSourceType(TypeDeclarationSyntax updatedType, UsingDirectiveSyntax? alias)
    {
        if (alias is null)
        {
            return _sourceRoot.ReplaceNode(_sourceType, updatedType);
        }

        var namespaceScope = _scopes[0].Declaration;
        var firstMember = namespaceScope.Members[0];
        var leading = firstMember.GetLeadingTrivia();
        // The last #endif of a namespace header or using block can be owned by
        // the first member. Place the alias after those existing closures.
        var lastClosure = Directives(leading).OfType<EndIfDirectiveTriviaSyntax>().LastOrDefault(directive =>
        {
            var group = directive.GetRelatedDirectives();
            return group.Count > 1 && (group[0].SpanStart < firstMember.FullSpan.Start ||
                ContainsOnlyInactiveImports(leading, ConditionalSpan(group)));
        });
        var precedingTriviaCount = lastClosure is null
            ? 0
            : leading.TakeWhile(trivia => trivia.SpanStart <= lastClosure.SpanStart).Count();
        SyntaxNode[] edits = [namespaceScope, _sourceType];
        return _sourceRoot.ReplaceNodes(edits, (_, rewritten) =>
        {
            if (rewritten is not BaseNamespaceDeclarationSyntax ns)
            {
                return updatedType;
            }

            var aliasWithTrivia = alias;
            if (precedingTriviaCount > 0)
            {
                var member = ns.Members[0];
                var trivia = member.GetLeadingTrivia();
                aliasWithTrivia = alias.WithLeadingTrivia(trivia.Take(precedingTriviaCount));
                ns = ns.WithMembers(ns.Members.Replace(member, member.WithLeadingTrivia(trivia.Skip(precedingTriviaCount))));
            }

            return ns.WithUsings(ns.Usings.Add(aliasWithTrivia));
        });
    }

    private static bool ContainsOnlyInactiveImports(SyntaxTriviaList leading, TextSpan span)
    {
        var hasImports = false;
        foreach (var trivia in leading.Where(item => item.IsKind(SyntaxKind.DisabledTextTrivia) && span.Contains(item.FullSpan)))
        {
            var root = SyntaxFactory.ParseCompilationUnit(trivia.ToFullString());
            if (root.ContainsDiagnostics || root.Members.Count > 0 || root.AttributeLists.Count > 0)
            {
                return false;
            }

            hasImports |= root.Externs.Count > 0 || root.Usings.Count > 0;
        }

        return hasImports;
    }

    internal static ExpressionSyntax AsExpression(NameSyntax name) =>
        name is QualifiedNameSyntax dotted
            ? SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, AsExpression(dotted.Left), dotted.DotToken, dotted.Right)
            : name;

    private static Header GetHeader(BaseNamespaceDeclarationSyntax scope)
    {
        var delimiter = GetOpeningDelimiter(scope);
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
        var selected = InGroups(header).Concat(bodyPrefix).ToArray();
        var otherDelimiter = delimiter.IsKind(SyntaxKind.SemicolonToken) ? SyntaxKind.OpenBraceToken : SyntaxKind.SemicolonToken;
        var normalizeToBlock = selected.Select(InactiveDelimiter).Any(token => token.IsKind(otherDelimiter));

        return new Header(InGroups(scope.NamespaceKeyword.LeadingTrivia), InGroups(delimiter.LeadingTrivia),
            delimitersBodyPrefix, selected, spans.Length > 0, normalizeToBlock);

        SyntaxTrivia[] InGroups(IEnumerable<SyntaxTrivia> trivia) =>
            trivia.Where(item => spans.Any(span => span.Contains(item.FullSpan))).ToArray();
    }

    private static SyntaxToken GetOpeningDelimiter(BaseNamespaceDeclarationSyntax scope) => scope switch
    {
        NamespaceDeclarationSyntax block => block.OpenBraceToken,
        FileScopedNamespaceDeclarationSyntax fileScoped => fileScoped.SemicolonToken,
        _ => default,
    };

    private static SyntaxToken InactiveDelimiter(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.DisabledTextTrivia))
        {
            return default;
        }

        // Only the first punctuation after a possible namespace name is its
        // delimiter. A later semicolon may belong to an inactive import.
        var token = SyntaxFactory.ParseTokens(trivia.ToFullString()).FirstOrDefault(token =>
            token.Kind() is not (SyntaxKind.NamespaceKeyword or SyntaxKind.IdentifierToken or SyntaxKind.DotToken));
        return token.Kind() is SyntaxKind.SemicolonToken or SyntaxKind.OpenBraceToken ? token : default;
    }

    internal CompilationUnitSyntax Wrap(TypeDeclarationSyntax type)
    {
        MemberDeclarationSyntax member = type;
        foreach (var (scope, header) in _scopes)
        {
            var keyword = scope.NamespaceKeyword.WithLeadingTrivia(_orphanDirectives.CopyTrivia(header.KeywordPrefix));
            var name = _orphanDirectives.CopyNode(scope.Name);
            var sourceDelimiter = GetOpeningDelimiter(scope);
            // Give every branch of a mixed header a block opener and one shared
            // close. The original conditional tail can also contain unrelated declarations.
            var delimiter = (header.NormalizeToBlock ? SyntaxFactory.Token(SyntaxKind.OpenBraceToken) : sourceDelimiter)
                .WithLeadingTrivia(_orphanDirectives.CopyTrivia(header.DelimiterPrefix))
                .WithTrailingTrivia(sourceDelimiter.TrailingTrivia.AddRange(_orphanDirectives.CopyTrivia(header.BodyPrefix)));
            BaseNamespaceDeclarationSyntax shell = delimiter.IsKind(SyntaxKind.SemicolonToken)
                ? SyntaxFactory.FileScopedNamespaceDeclaration(name).WithNamespaceKeyword(keyword)
                    .WithSemicolonToken(delimiter)
                : SyntaxFactory.NamespaceDeclaration(name).WithNamespaceKeyword(keyword)
                    .WithOpenBraceToken(delimiter);

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
        SyntaxNode[] copiedNodes, SyntaxTrivia[] headerTrivia, DirectiveTriviaSyntax[] fileSymbolDirectives,
        Dictionary<int, int> inactiveSemicolons)
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

        return new OrphanDirectiveRewriter(omitted, omittedBranches, inactiveSemicolons);
    }

    private static IEnumerable<DirectiveTriviaSyntax> Directives(IEnumerable<SyntaxTrivia> trivia) =>
        trivia.Where(item => item.IsDirective).Select(item => item.GetStructure()).OfType<DirectiveTriviaSyntax>();

    private static TextSpan ConditionalSpan(IReadOnlyList<DirectiveTriviaSyntax> group) =>
        TextSpan.FromBounds(group[0].FullSpan.Start, group[^1].FullSpan.End);

    private sealed record Header(
        SyntaxTrivia[] KeywordPrefix, SyntaxTrivia[] DelimiterPrefix, SyntaxTrivia[] BodyPrefix,
        SyntaxTrivia[] SelectedTrivia, bool IsConditional, bool NormalizeToBlock);

    private sealed class OrphanDirectiveRewriter(
        HashSet<int> omitted, List<TextSpan> omittedBranches, Dictionary<int, int> inactiveSemicolons) : CSharpSyntaxRewriter
    {
        internal SyntaxList<T> Copy<T>(IEnumerable<T> directives) where T : SyntaxNode =>
            SyntaxFactory.List(directives.Select(CopyNode));

        internal T CopyNode<T>(T node) where T : SyntaxNode =>
            Visit(node) is T rewritten ? rewritten : throw InvalidOperationErrors.Internal("Expected to retain the projected scope node.");

        internal SyntaxTriviaList CopyTrivia(IEnumerable<SyntaxTrivia> trivia) =>
            SyntaxFactory.TriviaList(trivia.Select(VisitTrivia).Where(item => item.RawKind != 0));

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsDirective && omitted.Contains(trivia.SpanStart) ||
                trivia.IsKind(SyntaxKind.DisabledTextTrivia) && omittedBranches.Any(branch => branch.Contains(trivia.Span)))
            {
                return default;
            }

            if (trivia.IsKind(SyntaxKind.DisabledTextTrivia) && inactiveSemicolons.TryGetValue(trivia.SpanStart, out var semicolon))
            {
                var text = trivia.ToFullString();
                return SyntaxFactory.DisabledText(text[..semicolon] + "{" + text[(semicolon + 1)..]);
            }

            return trivia;
        }
    }
}
