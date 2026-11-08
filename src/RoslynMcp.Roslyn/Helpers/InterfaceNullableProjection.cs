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
    private sealed record Slot(SyntaxAnnotation Annotation, ITypeSymbol Type);
    private sealed record Member(SyntaxAnnotation Annotation, ISymbol Source);
    private readonly List<Slot> _slots = [];
    private readonly List<Member> _members = [];
    private DocumentId? _sourceDocumentId;

    internal void SetSourceDocument(DocumentId id) => _sourceDocumentId = id;

    internal MemberDeclarationSyntax Capture(MemberDeclarationSyntax declaration, ISymbol symbol)
    {
        var roles = new List<(TypeSyntax Syntax, ITypeSymbol Type)>();
        void Parameters(SeparatedSyntaxList<ParameterSyntax> syntax, ImmutableArray<IParameterSymbol> parameters)
        {
            // `__arglist` is a typeless ParameterSyntax with no IParameterSymbol: it carries no type slot.
            var typed = syntax.Where(p => !p.Identifier.IsKind(SyntaxKind.ArgListKeyword)).ToArray();
            if (typed.Length != parameters.Length) throw InvalidOperationErrors.Internal("Generated interface parameter arity does not match its source roles.");
            for (var i = 0; i < typed.Length; i++)
                roles.Add((typed[i].Type ?? throw InvalidOperationErrors.Internal("Generated interface parameter type is missing."), parameters[i].Type));
        }
        switch (declaration, symbol)
        {
            case (MethodDeclarationSyntax method, IMethodSymbol original):
                roles.Add((method.ReturnType, original.ReturnType));
                Parameters(method.ParameterList.Parameters, original.Parameters);
                break;
            case (PropertyDeclarationSyntax property, IPropertySymbol { IsIndexer: false } original):
                roles.Add((property.Type, original.Type));
                break;
            case (IndexerDeclarationSyntax indexer, IPropertySymbol original):
                roles.Add((indexer.Type, original.Type));
                Parameters(indexer.ParameterList.Parameters, original.Parameters);
                break;
            case (EventFieldDeclarationSyntax evt, IEventSymbol original) when evt.Declaration.Variables.Count == 1:
                roles.Add((evt.Declaration.Type, original.Type));
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
                _slots.Add(new Slot(annotation, type));
                if (!tokens.TryGetValue(token, out var annotations)) tokens.Add(token, annotations = []);
                annotations.Add(annotation);
            });
        }
        var member = new SyntaxAnnotation("interface-nullable-member", Guid.NewGuid().ToString("N"));
        _members.Add(new Member(member, symbol));
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

    /// <summary>
    /// Decorates the generated interface with the nullable directives its receiver needs, then proves the final document is
    /// legal under the receiving project's language version. <paramref name="sourceParseOptions"/> are the options the members
    /// were authored under: any error the generated tree gains from the receiving options alone is a language the receiver cannot parse.
    /// </summary>
    internal async Task<Solution> ApplyAsync(Document receivingDocument, CompilationUnitSyntax constructedRoot, CSharpParseOptions sourceParseOptions, CancellationToken ct)
    {
        // Validate identities on the constructed, normalized tree, before any parser recovery.
        var declaration = constructedRoot.DescendantNodes().OfType<InterfaceDeclarationSyntax>().SingleOrDefault();
        if (declaration is null || declaration.Members.Count != _members.Count ||
            declaration.Members.Any(m => !_members.Any(role => m.HasAnnotation(role.Annotation))) ||
            _members.Any(role => constructedRoot.GetAnnotatedNodes(role.Annotation).Count() != 1))
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
        var solution = receivingDocument.Project.Solution;
        var baseline = receivingDocument;
        var model = await baseline.GetSemanticModelAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Generated interface semantic context is unavailable.");
        if (slots.All(s => Enabled(model, s.Token.SpanStart) == s.Enabled))
        {
            await EnsureReceivingLanguageAsync(baseline, sourceParseOptions, ct).ConfigureAwait(false);
            var members = ValidateMemberShapes(constructedRoot, model, position => position);
            await InterfaceHandlerBindingValidator.ValidateAsync(baseline, _sourceDocumentId, members, ct).ConfigureAwait(false);
            return solution;
        }

        foreach (var group in slots.GroupBy(s => s.Token.Span))
            if (group.Select(s => s.Enabled).Distinct().Count() != 1)
                throw InvalidOperationErrors.Internal("Generated interface type slot has contradictory annotation roles.");
        var edits = PlanDirectives(declaration, slots.Select(s => (s.Token, s.Enabled)).ToArray(), model, baselineText);
        var projection = new TextProjection(baselineText, edits, (CSharpParseOptions?)baseline.Project.ParseOptions);
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
        // Directives change only declarations: the generated interface is new to this solution, so no existing method body binds
        // against it yet, and parse plus declaration diagnostics observe every error the decoration can add (CS8370, CS8632,
        // implementation-nullability mismatches) without binding every method body in the receiving project.
        var added = AddedErrors(SignatureDiagnostics(baselineCompilation, ct), SignatureDiagnostics(decoratedCompilation, ct), LocationKey);
        if (added.Length > 0)
            throw new PublicInvalidOperationException("The nullable contract cannot be preserved under the receiving compiler policy (" +
                FormatIds(added) + "). Use a compatible receiving compiler policy.");
        await EnsureReceivingLanguageAsync(decorated, sourceParseOptions, ct).ConfigureAwait(false);
        var decoratedMembers = ValidateMemberShapes(constructedRoot, decoratedModel, projection.Forward);
        await InterfaceHandlerBindingValidator.ValidateAsync(decorated, _sourceDocumentId, decoratedMembers, ct).ConfigureAwait(false);
        return decoratedSolution;
    }

    private IReadOnlyList<(ISymbol Source, ISymbol Projected, MemberDeclarationSyntax Declaration)> ValidateMemberShapes(
        CompilationUnitSyntax constructedRoot, SemanticModel finalModel, Func<int, int> forward)
    {
        var mapped = new List<(ISymbol Source, ISymbol Projected, MemberDeclarationSyntax Declaration)>();
        var finalRoot = finalModel.SyntaxTree.GetRoot();
        var finalInterface = finalRoot.DescendantNodes().OfType<InterfaceDeclarationSyntax>().SingleOrDefault();
        if (finalInterface is null)
            throw InvalidOperationErrors.Internal("Generated interface member mapping is incomplete.");
        foreach (var role in _members)
        {
            var original = constructedRoot.GetAnnotatedNodes(role.Annotation).OfType<MemberDeclarationSyntax>().Single();
            var matches = finalInterface.Members.Where(member => member.RawKind == original.RawKind && member.SpanStart == forward(original.SpanStart)).ToArray();
            if (matches.Length != 1)
                throw InvalidOperationErrors.Internal("Generated interface member mapping is incomplete.");
            SyntaxNode declaration = matches[0] is EventFieldDeclarationSyntax field && field.Declaration.Variables.Count == 1
                ? field.Declaration.Variables[0]
                : matches[0];
            var projected = finalModel.GetDeclaredSymbol(declaration);
            if (projected is null || !SameMemberShape(role.Source, projected))
                throw new PublicInvalidOperationException("The generated interface nullable contract does not match its source member.");
            mapped.Add((role.Source, projected, matches[0]));
        }
        return mapped;
    }

    private static bool SameMemberShape(ISymbol source, ISymbol projected)
    {
        static bool Parameters(ImmutableArray<IParameterSymbol> left, ImmutableArray<IParameterSymbol> right) =>
            left.Length == right.Length && left.Zip(right).All(pair => NullableShape(pair.First.Type) == NullableShape(pair.Second.Type));
        return (source, projected) switch
        {
            (IMethodSymbol left, IMethodSymbol right) => NullableShape(left.ReturnType) == NullableShape(right.ReturnType) && Parameters(left.Parameters, right.Parameters),
            (IPropertySymbol left, IPropertySymbol right) => NullableShape(left.Type) == NullableShape(right.Type) && Parameters(left.Parameters, right.Parameters),
            (IEventSymbol left, IEventSymbol right) => NullableShape(left.Type) == NullableShape(right.Type),
            _ => false,
        };
    }

    private static string NullableShape(ITypeSymbol type)
    {
        var children = type switch
        {
            IArrayTypeSymbol array => [array.ElementType],
            IPointerTypeSymbol pointer => [pointer.PointedAtType],
            IFunctionPointerTypeSymbol function => function.Signature.Parameters.Select(p => p.Type).Prepend(function.Signature.ReturnType).ToArray(),
            INamedTypeSymbol named => named.TypeArguments.Concat(named.ContainingType is null ? [] : [named.ContainingType]).ToArray(),
            _ => Array.Empty<ITypeSymbol>(),
        };
        return type.NullableAnnotation + "[" + string.Join(",", children.Select(NullableShape)) + "]";
    }

    /// <summary>
    /// Binds the final generated tree twice over the same references and compilation options, differing only in parse options:
    /// once as the receiving project parses it and once as the source project would. Errors only the receiving parse gains are language features the
    /// receiver cannot compile (the compiler's own ids decide; nothing is synthesized). Errors both parses share (unbound types,
    /// other policy) are not a language delta and pass through. Only default-severity errors are compared, so an editorconfig
    /// severity attached to one tree cannot manufacture a difference.
    /// </summary>
    private static async Task EnsureReceivingLanguageAsync(Document finalDocument, CSharpParseOptions sourceParseOptions, CancellationToken ct)
    {
        if (finalDocument.Project.ParseOptions?.Equals(sourceParseOptions) == true) return;
        var compilation = await finalDocument.Project.GetCompilationAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Generated interface compilation is unavailable.");
        var receivingTree = await finalDocument.GetSyntaxTreeAsync(ct).ConfigureAwait(false) ?? throw InvalidOperationErrors.Internal("Generated interface syntax tree is unavailable.");
        // A compilation requires one language version across its trees, so the source-options view re-parses every tree (same text and
        // paths, same references and compilation options): the generated interface then binds against the same sibling types in both.
        var receivingTrees = compilation.SyntaxTrees.ToArray();
        var index = Array.IndexOf(receivingTrees, receivingTree);
        if (index < 0) throw InvalidOperationErrors.Internal("Generated interface syntax tree is not part of its compilation.");
        var sourceTrees = receivingTrees.Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(ct), sourceParseOptions, tree.FilePath, ct)).ToArray();
        var sourceTree = sourceTrees[index];
        var receiving = compilation.GetSemanticModel(receivingTree).GetDiagnostics(cancellationToken: ct);
        var source = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(sourceTrees).GetSemanticModel(sourceTree).GetDiagnostics(cancellationToken: ct);
        var added = AddedErrors(source.Where(d => d.DefaultSeverity == DiagnosticSeverity.Error), receiving.Where(d => d.DefaultSeverity == DiagnosticSeverity.Error),
            (location, _) => TreeIndependentLocationKey(location));
        if (added.Length > 0)
            throw new PublicInvalidOperationException("The generated interface uses language features the receiving project's C# language version does not support (" +
                FormatIds(added) + "). Raise the receiving project's LangVersion or extract into a project with a newer language version.");
    }

    /// <summary>Location identity by document path and span: the same text parsed twice yields different tree objects.</summary>
    internal static string TreeIndependentLocationKey(Location location) => location.IsInSource
        ? JsonSerializer.Serialize(new { Path = location.SourceTree!.FilePath, Start = location.SourceSpan.Start, End = location.SourceSpan.End })
        : JsonSerializer.Serialize(new { location.Kind, Line = location.GetLineSpan().ToString() });

    /// <summary>Every distinct diagnostic id, ordered; a refusal names all of them, never a prefix.</summary>
    internal static string FormatIds(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(", ", diagnostics.Select(d => d.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static IEnumerable<Diagnostic> SignatureDiagnostics(Compilation compilation, CancellationToken ct) =>
        compilation.GetParseDiagnostics(ct).Concat(compilation.GetDeclarationDiagnostics(ct));

    /// <summary>
    /// Plans whole-line directives. A member with a mismatched slot whose slots all want one state is wrapped as a unit, and adjacent
    /// members wanting the same state share one directive; after a run the explicit baseline state is re-established.
    /// Only a member whose own slots want different states is decorated per slot.
    /// </summary>
    private static (int Position, string Text)[] PlanDirectives(InterfaceDeclarationSyntax declaration, (SyntaxToken Token, bool Enabled)[] slots, SemanticModel model, string text)
    {
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SourceText.From(text).Lines;
        var edits = new List<(int Position, string Text)>();
        string Directive(bool enabled) => "#nullable " + (enabled ? "enable" : "disable") + " annotations" + eol;
        // A directive owns a line: take the line start when only indentation precedes the position, else break the line.
        void Before(int position, bool enabled)
        {
            var start = lines.GetLineFromPosition(position).Start;
            if (string.IsNullOrWhiteSpace(text[start..position])) edits.Add((start, Directive(enabled)));
            else edits.Add((position, eol + Directive(enabled)));
        }
        bool? forced = null;
        foreach (var member in declaration.Members)
        {
            var own = slots.Where(s => member.Span.Contains(s.Token.Span)).ToArray();
            var baseline = Enabled(model, member.SpanStart);
            var states = own.Select(s => s.Enabled).Distinct().ToArray();
            bool? wanted = own.Any(s => Enabled(model, s.Token.SpanStart) != s.Enabled) && states.Length == 1 ? states[0] : null;
            if (wanted is bool state)
            {
                if ((forced ?? baseline) != state) Before(member.SpanStart, state);
                forced = state;
                continue;
            }
            if (forced is bool previous && previous != baseline) Before(member.SpanStart, baseline);
            forced = null;
            foreach (var slot in own.Where(s => Enabled(model, s.Token.SpanStart) != s.Enabled))
            {
                edits.Add((slot.Token.SpanStart, eol + Directive(slot.Enabled)));
                edits.Add((slot.Token.Span.End, eol + Directive(Enabled(model, slot.Token.Span.End))));
            }
        }
        if (forced is bool last && last != Enabled(model, declaration.CloseBraceToken.SpanStart))
            Before(declaration.CloseBraceToken.SpanStart, !last);
        return edits.ToArray();
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
            _edits = edits.OrderBy(e => e.Position).ToArray(); // Stable: same-position edits keep plan order.
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
