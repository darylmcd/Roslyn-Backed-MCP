using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Transfers annotation contexts for the type slots the producer actually emits.</summary>
internal sealed class InterfaceNullableProjection
{
    internal enum TypeRole { Return, Parameter, Property, IndexerKey, Event }
    private sealed record Slot(SyntaxAnnotation Annotation, ITypeSymbol Type, TypeRole Role);
    private readonly List<Slot> _slots = [];
    private readonly List<SyntaxAnnotation> _members = [];

    internal MemberDeclarationSyntax Capture(MemberDeclarationSyntax declaration, ISymbol symbol)
    {
        var roles = new List<(TypeSyntax Syntax, ITypeSymbol Type, TypeRole Role)>();
        void Parameters(SeparatedSyntaxList<ParameterSyntax> syntax, ImmutableArray<IParameterSymbol> parameters, TypeRole role)
        {
            if (syntax.Count != parameters.Length) throw InvalidOperationErrors.Internal("Generated interface parameter arity does not match its source roles.");
            for (var i = 0; i < syntax.Count; i++)
                roles.Add((syntax[i].Type ?? throw InvalidOperationErrors.Internal("Generated interface parameter type is missing."), parameters[i].Type, role));
        }
        switch (declaration, symbol)
        {
            case (MethodDeclarationSyntax method, IMethodSymbol original):
                roles.Add((method.ReturnType, original.ReturnType, TypeRole.Return));
                Parameters(method.ParameterList.Parameters, original.Parameters, TypeRole.Parameter);
                break;
            case (PropertyDeclarationSyntax property, IPropertySymbol original):
                // Legacy producers emit indexers as properties: only the emitted type is a role.
                roles.Add((property.Type, original.Type, TypeRole.Property));
                break;
            case (IndexerDeclarationSyntax indexer, IPropertySymbol original):
                roles.Add((indexer.Type, original.Type, TypeRole.Property));
                Parameters(indexer.ParameterList.Parameters, original.Parameters, TypeRole.IndexerKey);
                break;
            case (EventFieldDeclarationSyntax evt, IEventSymbol original) when evt.Declaration.Variables.Count == 1:
                roles.Add((evt.Declaration.Type, original.Type, TypeRole.Event));
                break;
            default:
                throw InvalidOperationErrors.Internal("Generated interface declaration does not match its source roles.");
        }
        var tokens = new Dictionary<SyntaxToken, List<SyntaxAnnotation>>();
        foreach (var role in roles)
        {
            Walk(role.Syntax, role.Type, false, (token, type) =>
            {
                var annotation = new SyntaxAnnotation("interface-nullable-slot", Guid.NewGuid().ToString("N"));
                _slots.Add(new Slot(annotation, type, role.Role));
                if (!tokens.TryGetValue(token, out var annotations)) tokens.Add(token, annotations = []);
                annotations.Add(annotation);
            });
        }
        var member = new SyntaxAnnotation("interface-nullable-member", Guid.NewGuid().ToString("N"));
        _members.Add(member);
        return declaration.ReplaceTokens(tokens.Keys, (original, rewritten) => rewritten.WithAdditionalAnnotations(tokens[original].ToArray()))
            .WithAdditionalAnnotations(member);
    }

    private static void Walk(TypeSyntax syntax, ITypeSymbol symbol, bool qualifier, Action<SyntaxToken, ITypeSymbol> capture)
    {
        if (syntax is RefTypeSyntax reference) { Walk(reference.Type, symbol, qualifier, capture); return; }
        if (!qualifier && syntax is not ArrayTypeSyntax && (symbol.IsReferenceType || symbol.TypeKind == TypeKind.TypeParameter))
            capture(syntax.GetLastToken(), symbol);
        if (syntax is NullableTypeSyntax nullable)
        {
            var element = symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } value ? value.TypeArguments[0] : symbol;
            Children(nullable.ElementType, element, capture);
            return;
        }
        Children(syntax, symbol, capture);
    }

    private static void Children(TypeSyntax syntax, ITypeSymbol symbol, Action<SyntaxToken, ITypeSymbol> capture)
    {
        switch (syntax)
        {
            case ArrayTypeSyntax array:
                ITypeSymbol element = symbol;
                foreach (var rank in array.RankSpecifiers)
                {
                    if (element is not IArrayTypeSymbol current || current.Rank != rank.Rank)
                        throw InvalidOperationErrors.Internal("Generated interface array shape does not match its source role.");
                    capture(rank.CloseBracketToken, current);
                    element = current.ElementType;
                }
                Walk(array.ElementType, element, false, capture);
                return;
            case PointerTypeSyntax pointer when symbol is IPointerTypeSymbol original:
                Walk(pointer.ElementType, original.PointedAtType, false, capture);
                return;
            case FunctionPointerTypeSyntax function when symbol is IFunctionPointerTypeSymbol original:
                var types = original.Signature.Parameters.Select(p => p.Type).Append(original.Signature.ReturnType).ToArray();
                if (function.ParameterList.Parameters.Count != types.Length)
                    throw InvalidOperationErrors.Internal("Generated interface function pointer arity does not match its source role.");
                for (var i = 0; i < types.Length; i++) Walk(function.ParameterList.Parameters[i].Type, types[i], false, capture);
                return;
            case TupleTypeSyntax tuple when symbol is INamedTypeSymbol { IsTupleType: true } original:
                if (tuple.Elements.Count != original.TupleElements.Length)
                    throw InvalidOperationErrors.Internal("Generated interface tuple arity does not match its source role.");
                for (var i = 0; i < tuple.Elements.Count; i++) Walk(tuple.Elements[i].Type, original.TupleElements[i].Type, false, capture);
                return;
            case QualifiedNameSyntax qualified when symbol is INamedTypeSymbol named:
                if (named.ContainingType is not null) Walk(qualified.Left, named.ContainingType, true, capture);
                else if (qualified.Left.DescendantNodesAndSelf().OfType<GenericNameSyntax>().Any())
                    throw InvalidOperationErrors.Internal("Generated interface generic qualifier has no source role.");
                Children(qualified.Right, named, capture);
                return;
            case AliasQualifiedNameSyntax alias:
                Children(alias.Name, symbol, capture);
                return;
            case GenericNameSyntax generic when symbol is INamedTypeSymbol named:
                if (generic.TypeArgumentList.Arguments.Count != named.TypeArguments.Length)
                    throw InvalidOperationErrors.Internal("Generated interface generic arity does not match its source role.");
                for (var i = 0; i < named.TypeArguments.Length; i++) Walk(generic.TypeArgumentList.Arguments[i], named.TypeArguments[i], false, capture);
                return;
            case IdentifierNameSyntax or PredefinedTypeSyntax:
                return;
            default:
                throw InvalidOperationErrors.Internal("Generated interface type shape does not match its source role.");
        }
    }

    internal async Task<Solution> ApplyAsync(Document receivingDocument, CompilationUnitSyntax constructedRoot, CancellationToken ct)
    {
        // Validate identities on the constructed, normalized tree, before any parser recovery.
        var declaration = constructedRoot.DescendantNodes().OfType<InterfaceDeclarationSyntax>().SingleOrDefault();
        if (declaration is null || declaration.Members.Count != _members.Count ||
            declaration.Members.Any(m => !_members.Any(m.HasAnnotation)) ||
            _members.Any(a => constructedRoot.GetAnnotatedNodes(a).Count() != 1))
            throw InvalidOperationErrors.Internal("Generated interface member mapping is incomplete.");
        var baselineText = constructedRoot.ToFullString();
        if ((await receivingDocument.GetTextAsync(ct).ConfigureAwait(false)).ToString() != baselineText)
            throw InvalidOperationErrors.Internal("Generated interface text does not match its construction mapping.");
        var slots = _slots.Select(slot =>
        {
            var token = constructedRoot.GetAnnotatedTokens(slot.Annotation).SingleOrDefault();
            if (token.RawKind == 0 || token.Span.Length == 0 || baselineText.Substring(token.SpanStart, token.Span.Length) != token.Text)
                throw InvalidOperationErrors.Internal("Generated interface type slot mapping is incomplete.");
            return (Token: token, Enabled: slot.Type.NullableAnnotation != NullableAnnotation.None);
        }).ToArray();
        var solution = receivingDocument.Project.Solution.WithDocumentText(receivingDocument.Id, SourceText.From(baselineText));
        var baseline = solution.GetDocument(receivingDocument.Id) ?? throw InvalidOperationErrors.Internal("Generated interface document is unavailable.");
        var model = await baseline.GetSemanticModelAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Generated interface semantic context is unavailable.");
        if (slots.All(s => Enabled(model, s.Token.SpanStart) == s.Enabled)) return receivingDocument.Project.Solution;

        var edits = new SortedDictionary<int, StringBuilder>();
        void Insert(int position, string text)
        {
            if (!edits.TryGetValue(position, out var builder)) edits.Add(position, builder = new StringBuilder());
            builder.Append(text);
        }
        foreach (var group in slots.GroupBy(s => s.Token.Span).OrderBy(s => s.Key.Start))
        {
            if (group.Select(s => s.Enabled).Distinct().Count() != 1)
                throw InvalidOperationErrors.Internal("Generated interface type slot has contradictory annotation roles.");
            Insert(group.Key.Start, "\n#nullable " + (group.First().Enabled ? "enable" : "disable") + " annotations\n");
            Insert(group.Key.End, "\n#nullable restore annotations\n");
        }
        var projection = new TextProjection(baselineText, edits.Select(e => (e.Key, e.Value.ToString())).ToArray(), (CSharpParseOptions?)baseline.Project.ParseOptions);
        var decoratedSolution = solution.WithDocumentText(baseline.Id, SourceText.From(projection.Text));
        var decorated = decoratedSolution.GetDocument(baseline.Id) ?? throw InvalidOperationErrors.Internal("Decorated interface document is unavailable.");
        var decoratedModel = await decorated.GetSemanticModelAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Decorated interface semantic context is unavailable.");
        if (slots.Any(s => Enabled(decoratedModel, projection.Forward(s.Token.SpanStart)) != s.Enabled))
            throw InvalidOperationErrors.Internal("Generated interface annotation context projection is incomplete.");
        var baselineCompilation = await baseline.Project.GetCompilationAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Generated interface compilation is unavailable.");
        var decoratedCompilation = await decorated.Project.GetCompilationAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Decorated interface compilation is unavailable.");
        var baselineTree = await baseline.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        var decoratedTree = await decorated.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        string LocationKey(Location location, bool after)
        {
            if (!location.IsInSource) return JsonSerializer.Serialize(new { location.Kind, Line = location.GetLineSpan().ToString() });
            var tree = location.SourceTree;
            var target = tree == (after ? decoratedTree : baselineTree);
            var documentId = (after ? decoratedSolution : solution).GetDocumentId(tree!)?.ToString() ?? tree?.FilePath ?? "";
            return JsonSerializer.Serialize(new
            {
                documentId,
                Start = target ? projection.Anchor(location.SourceSpan.Start, after, end: false) : location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture),
                End = target ? projection.Anchor(location.SourceSpan.End, after, end: true) : location.SourceSpan.End.ToString(CultureInfo.InvariantCulture)
            });
        }
        var added = AddedErrors(baselineCompilation.GetDiagnostics(ct), decoratedCompilation.GetDiagnostics(ct), LocationKey);
        if (added.Length > 0)
            throw new PublicInvalidOperationException("The nullable contract cannot be preserved under the receiving compiler policy (" +
                string.Join(", ", added.Select(d => d.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(5)) +
                "). Use a compatible receiving compiler policy.");
        return decoratedSolution;
    }

    private static bool Enabled(SemanticModel model, int position) => (model.GetNullableContext(position) & NullableContext.AnnotationsEnabled) != 0;

    internal static Diagnostic[] AddedErrors(IEnumerable<Diagnostic> baseline, IEnumerable<Diagnostic> decorated, Func<Location, bool, string> locationKey)
    {
        string Key(Diagnostic d, bool after) => JsonSerializer.Serialize(new
        {
            d.Id,
            d.Severity,
            d.DefaultSeverity,
            d.IsWarningAsError,
            Message = d.GetMessage(CultureInfo.InvariantCulture),
            Properties = d.Properties.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Location = locationKey(d.Location, after),
            Additional = d.AdditionalLocations.Select(l => locationKey(l, after)).ToArray(),
        });
        var counts = baseline.Where(d => d.Severity == DiagnosticSeverity.Error).GroupBy(d => Key(d, false)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var added = new List<Diagnostic>();
        foreach (var diagnostic in decorated.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var key = Key(diagnostic, true);
            if (counts.TryGetValue(key, out var count) && count > 0) counts[key] = count - 1;
            else added.Add(diagnostic);
        }
        return added.ToArray();
    }

    /// <summary>Insertion-only text mapping: original lexical/trivia identities survive parser recovery.</summary>
    internal sealed class TextProjection
    {
        private readonly (int Position, string Text)[] _edits;
        private readonly SyntaxToken[] _tokens;
        private readonly string _original;
        internal string Text { get; }

        internal TextProjection(string original, (int Position, string Text)[] edits, CSharpParseOptions? options)
        {
            _original = original;
            _edits = edits.OrderBy(e => e.Position).ToArray();
            _tokens = SyntaxFactory.ParseTokens(original, options: options).ToArray();
            var builder = new StringBuilder();
            var previous = 0;
            foreach (var edit in _edits)
            {
                builder.Append(original, previous, edit.Position - previous).Append(edit.Text);
                previous = edit.Position;
            }
            Text = builder.Append(original, previous, original.Length - previous).ToString();
            var actual = SyntaxFactory.ParseTokens(Text, options: options).Select(t => (t.RawKind, t.Text)).ToArray();
            if (!_tokens.Select(t => (t.RawKind, t.Text)).SequenceEqual(actual))
                throw InvalidOperationErrors.Internal("Nullable projection changed generated interface lexical tokens.");
        }

        internal int Forward(int position) => position + _edits.Where(e => e.Position <= position).Sum(e => e.Text.Length);

        internal string Anchor(int position, bool decorated, bool end)
        {
            if (decorated)
            {
                var shift = 0;
                for (var i = 0; i < _edits.Length; i++)
                {
                    var start = _edits[i].Position + shift;
                    if (position < start || (end && position == start)) break;
                    if (position < start + _edits[i].Text.Length) return "new:" + i + ":" + (position - start);
                    shift += _edits[i].Text.Length;
                }
                position -= shift;
            }
            if (position == _original.Length) return "original:eof";
            for (var i = 0; i < _tokens.Length; i++)
                if (position < _tokens[i].Span.End) return "original:token:" + i + ":" + (position - _tokens[i].SpanStart);
            return "original:tail:" + (position - _original.Length);
        }
    }
}
