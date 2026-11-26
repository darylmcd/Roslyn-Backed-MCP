using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class ChangeSignatureAddRemovePreviewBuilder
{
    private const string DeclarationTag = "signature-declaration";
    private const string BindingTag = "signature-binding";
    private const string ArgumentTag = "signature-argument";
    internal const string DiagnosticTag = "signature-diagnostic";
    private const string ConditionalTag = "signature-conditional";
    private const string AliasScopeTag = "signature-alias-scope";

    internal sealed class BoundCallContext
    {
        private readonly IMethodSymbol _method;
        private readonly SemanticModel _model;
        private readonly int _position;
        private readonly HashSet<string> _requiredAliases;
        private readonly ImmutableArray<IArgumentOperation> _operations;
        private readonly List<(int Ordinal, bool Expanded)> _bindings;

        internal bool RemovesExtensionReceiver { get; private set; }
        internal string TargetIdentity => SymbolIdentity(CanonicalDefinition(_method));
        internal string ContainingTypeName() => MaterializedTypeName(_method.ContainingType, _model, _position, _requiredAliases);

        internal BoundCallContext(
            IMethodSymbol method, ImmutableArray<IArgumentOperation> operations,
            SemanticModel model, SeparatedSyntaxList<ArgumentSyntax> arguments, int position, HashSet<string> requiredAliases)
        {
            _method = method;
            _model = model;
            _position = position;
            _requiredAliases = requiredAliases;
            _operations = operations;
            _bindings = [];
            foreach (var argument in arguments)
            {
                var bound = ArgumentOperationResolver.Resolve(model, argument.Expression, CancellationToken.None);
                if (bound?.Parameter is null)
                    throw Refuse("An existing argument has no original semantic parameter binding.");
                var expanded = bound.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection;
                _bindings.Add((bound.Parameter.Ordinal, expanded));
            }
        }

        internal SeparatedSyntaxList<ArgumentSyntax> Add(
            SeparatedSyntaxList<ArgumentSyntax> arguments, int position, string name, string value)
        {
            // Positional insertion is safe only with a complete, unexpanded, in-order prefix.
            // Otherwise name every original argument; the new constant/default is evaluated last.
            if (_operations.All(o => !o.IsImplicit) && _bindings.All(b => !b.Expanded)
                && arguments.All(a => a.NameColon is null)
                && arguments.Count == _method.Parameters.Length
                && _bindings.Select(b => b.Ordinal).SequenceEqual(Enumerable.Range(0, arguments.Count)))
                return arguments.Insert(position, SyntaxFactory.Argument(SyntaxFactory.ParseExpression(value))
                    .WithLeadingTrivia(position == 0 ? default : SyntaxFactory.Space));
            var named = NameOriginalArguments(arguments, null);
            return named.Add(NamedArgument(name, SyntaxFactory.ParseExpression(value))
                .WithLeadingTrivia(named.Count == 0 ? default : SyntaxFactory.Space));
        }

        internal SeparatedSyntaxList<ArgumentSyntax> Remove(SeparatedSyntaxList<ArgumentSyntax> arguments, int ordinal)
        {
            RemovesExtensionReceiver = ordinal == 0 && _method.IsExtensionMethod
                && _operations.Any(o => o.Parameter?.Ordinal == 0 && o.IsImplicit);
            // Remove every element bound to the parameter, including expanded params elements.
            if (arguments.All(a => a.NameColon is null)
                && _bindings.All(b => !b.Expanded)
                && arguments.Count == _method.Parameters.Length
                && _bindings.Select(b => b.Ordinal).SequenceEqual(Enumerable.Range(0, arguments.Count)))
            {
                var result = arguments;
                for (var i = arguments.Count - 1; i >= 0; i--)
                    if (_bindings[i].Ordinal == ordinal) result = result.RemoveAt(i);
                return result;
            }
            return NameOriginalArguments(arguments, ordinal);
        }

        internal SeparatedSyntaxList<ArgumentSyntax> Reorder(
            SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            if (arguments.Any(a => a.NameColon is not null) && arguments.Any(a => a.NameColon is null))
                throw Refuse("op='reorder' refuses to rewrite a callsite that mixes positional and named arguments. Convert it to all-positional or all-named, then retry.");
            // Naming preserves expression evaluation order; sorting expressions by the new
            // parameter order would change side effects, even when compilation still succeeds.
            return NameOriginalArguments(arguments, null);
        }

        internal ExpressionSyntax StaticExtensionTarget(ExpressionSyntax original)
        {
            var type = SyntaxFactory.ParseExpression(MaterializedTypeName(_method.ContainingType, _model, _position, _requiredAliases));
            var name = original switch
            {
                MemberAccessExpressionSyntax member => member.Name,
                MemberBindingExpressionSyntax member => member.Name,
                _ => throw Refuse("The extension target has no editable member syntax."),
            };
            name = RetainTypeArguments(name);
            return original switch
            {
                MemberAccessExpressionSyntax member => member.WithExpression(type.WithTriviaFrom(member.Expression)).WithName(name),
                MemberBindingExpressionSyntax member => member.CopyAnnotationsTo(SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, type, member.OperatorToken, name)),
                _ => throw Refuse("The extension target has no editable member syntax."),
            };
        }


        internal ExpressionSyntax RetainTypeSubstitution(ExpressionSyntax expression) => expression switch
        {
            SimpleNameSyntax name => RetainTypeArguments(name),
            MemberAccessExpressionSyntax member => member.WithName(RetainTypeArguments(member.Name)),
            MemberBindingExpressionSyntax member => member.WithName(RetainTypeArguments(member.Name)),
            _ => expression,
        };

        private SimpleNameSyntax RetainTypeArguments(SimpleNameSyntax name)
        {
            if (name is not IdentifierNameSyntax identifier || _method.Arity == 0) return name;
            var requiredAliases = new HashSet<string>(_requiredAliases, StringComparer.Ordinal);
            var types = _method.TypeArguments.Select(t => SyntaxFactory.ParseTypeName(MaterializedTypeName(t, _model, _position, requiredAliases))).ToArray();
            // Anonymous substitutions cannot be named. Leave inference intact and let
            // candidate binding validation distinguish retained inference from an unsafe edit.
            if (types.Any(t => t.ContainsDiagnostics)) return name;
            _requiredAliases.UnionWith(requiredAliases);
            return identifier.CopyAnnotationsTo(SyntaxFactory.GenericName(identifier.Identifier,
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(types))));
        }

        internal SeparatedSyntaxList<ArgumentSyntax> NameOriginalArguments(
            SeparatedSyntaxList<ArgumentSyntax> arguments, int? removedOrdinal)
        {
            var result = SyntaxFactory.SeparatedList<ArgumentSyntax>();
            var previousOriginal = -1;
            for (var i = 0; i < arguments.Count; i++)
            {
                var originalStart = i;
                var binding = _bindings[i];
                if (binding.Ordinal == removedOrdinal) continue;
                var parameter = _method.Parameters[binding.Ordinal];
                ArgumentSyntax argument;
                if (binding.Expanded)
                {
                    var elements = new List<ExpressionSyntax>();
                    var separators = new List<SyntaxToken>();
                    while (i < arguments.Count && _bindings[i].Expanded && _bindings[i].Ordinal == binding.Ordinal)
                    {
                        var expression = arguments[i].Expression;
                        elements.Add(expression);
                        if (i + 1 < arguments.Count && _bindings[i + 1].Expanded && _bindings[i + 1].Ordinal == binding.Ordinal)
                            separators.Add(arguments.GetSeparator(i));
                        i++;
                    }
                    i--;
                    ExpressionSyntax packed;
                    if (parameter.Type is IArrayTypeSymbol array)
                    {
                        packed = SyntaxFactory.ArrayCreationExpression(
                            SyntaxFactory.ArrayType(SyntaxFactory.ParseTypeName(
                                MaterializedTypeName(array.ElementType, _model, _position, _requiredAliases)),
                                SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))),
                            SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                                SyntaxFactory.SeparatedList(elements, separators)))
                            .WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                    }
                    else
                    {
                        packed = SyntaxFactory.CastExpression(
                            SyntaxFactory.ParseTypeName(MaterializedTypeName(parameter.Type, _model, _position, _requiredAliases)),
                            SyntaxFactory.ParenthesizedExpression(SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList<CollectionElementSyntax>(
                                elements.Select(SyntaxFactory.ExpressionElement), separators))));
                    }
                    argument = NamedArgument(parameter.Name, packed);
                }
                else
                {
                    argument = arguments[i].NameColon is not null ? arguments[i] : arguments[i].WithNameColon(SyntaxFactory.NameColon(
                        SyntaxFactory.IdentifierName(EscapeIdentifier(parameter.Name)))
                        .WithTrailingTrivia(SyntaxFactory.Space));
                }
                var previousCount = result.Count;
                result = result.Add(argument);
                if (previousCount > 0 && previousOriginal >= 0)
                {
                    var separator = arguments.GetSeparator(previousOriginal);
                    for (var skipped = previousOriginal + 1; skipped < originalStart; skipped++)
                    {
                        var orphan = arguments.GetSeparator(skipped);
                        separator = separator.WithTrailingTrivia(separator.TrailingTrivia
                            .AddRange(orphan.LeadingTrivia).AddRange(orphan.TrailingTrivia));
                    }
                    result = result.ReplaceSeparator(result.GetSeparator(previousCount - 1), separator);
                }
                previousOriginal = i;
            }

            // Freeze compiler-supplied values, including caller-info and caller-argument-expression.
            // Leaving them implicit can change values when an edit changes line numbers or syntax.
            foreach (var operation in _operations)
            {
                if (operation.Parameter is not { } parameter || parameter.Ordinal == removedOrdinal
                    || operation.ArgumentKind != ArgumentKind.DefaultValue) continue;
                // Struct defaults have no operation constant, but their optional metadata value is null.
                var originalValue = operation.Value.ConstantValue.HasValue ? operation.Value.ConstantValue.Value
                    : parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue
                    : throw Refuse("An omitted argument cannot be materialized from its original bound value.");
                var value = ConstantExpression(originalValue, parameter.Type, _model, _position, _requiredAliases);
                result = result.Add(NamedArgument(parameter.Name, value)
                    .WithLeadingTrivia(result.Count == 0 ? default : SyntaxFactory.Space));
            }
            return result;
        }
    }


    private sealed record ReadOutcome<T>(T? Value, ExceptionDispatchInfo? Failure)
    {
        public T? GetResult()
        {
            Failure?.Throw();
            return Value;
        }
    }

    private static async Task<ReadOutcome<TResult>[]> ReadBatchAsync<TInput, TResult>(
        IReadOnlyList<TInput> inputs, Func<TInput, Task<TResult>> read)
    {
        var inputCount = inputs.Count;
        var bound = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        var tasks = new Task<TResult>?[bound];
        var indexes = new int[bound];
        var outcomes = new ReadOutcome<TResult>[inputCount];
        var count = 0;
        var active = 0;
        var stopped = false;
        ExceptionDispatchInfo? coordinatorFailure = null;

        async Task<bool> CollectAsync(int slot)
        {
            var task = tasks[slot];
            if (task is null) return true;
            try
            {
                outcomes[indexes[slot]] = new ReadOutcome<TResult>(await task.ConfigureAwait(false), null);
                return true;
            }
            catch (Exception error)
            {
                outcomes[indexes[slot]] = new ReadOutcome<TResult>(default, ExceptionDispatchInfo.Capture(error));
                return false;
            }
            finally
            {
                tasks[slot] = null;
                active--;
            }
        }

        try
        {
            while (active > 0 || (!stopped && count < inputCount))
            {
                if (active > 0)
                {
                    var completed = Enumerable.Range(0, bound)
                        .Where(slot => tasks[slot]?.IsCompleted == true)
                        .OrderBy(slot => indexes[slot]).ToArray();
                    if (completed.Length == 0)
                    {
                        await Task.WhenAny(tasks.OfType<Task<TResult>>()).ConfigureAwait(false);
                        continue;
                    }
                    // Observe every completed sibling before admitting any replacement.
                    foreach (var slot in completed)
                        if (!await CollectAsync(slot).ConfigureAwait(false)) stopped = true;
                }
                for (var slot = 0; !stopped && slot < bound && count < inputCount; slot++)
                {
                    if (tasks[slot] is not null) continue;
                    var index = count++;
                    try
                    {
                        var input = inputs[index];
                        indexes[slot] = index;
                        // Reader cancellation is indexed data; scheduling never cancels ownership.
                        tasks[slot] = Task.Run(() => read(input), CancellationToken.None);
                        active++;
                    }
                    catch (Exception error)
                    {
                        outcomes[index] = new ReadOutcome<TResult>(default, ExceptionDispatchInfo.Capture(error));
                        stopped = true;
                    }
                }
            }
        }
        catch (Exception error)
        {
            coordinatorFailure = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            // Every owned handle joins, including after an unexpected coordinator failure.
            foreach (var slot in Enumerable.Range(0, bound).Where(slot => tasks[slot] is not null)
                .OrderBy(slot => indexes[slot]))
                await CollectAsync(slot).ConfigureAwait(false);
        }
        coordinatorFailure?.Throw();
        return count == inputCount ? outcomes : outcomes[..count];
    }

    private static void ConsumeReadOutcomes<T>(ReadOutcome<T>[] outcomes, Action<T?> consume, CancellationToken ct)
    {
        var bound = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        try
        {
            for (var index = 0; index < outcomes.Length; index++)
            {
                try
                {
                    if (index > 0 && index % bound == 0) ct.ThrowIfCancellationRequested();
                    consume(outcomes[index].GetResult());
                }
                finally
                {
                    Array.Clear(outcomes, index, 1);
                }
            }
        }
        finally
        {
            Array.Clear(outcomes);
        }
    }

    private sealed record DocumentScan(
        Document Document, SyntaxNode Root, SemanticModel Model,
        Dictionary<SyntaxToken, List<SyntaxAnnotation>> DiagnosticAnnotations,
        Dictionary<SyntaxNode, List<SyntaxAnnotation>> Annotations,
        Dictionary<SyntaxNode, BoundCallContext> Calls, HashSet<string> RequiredAliases,
        string[] SourceAliases, string ProjectAlias,
        Dictionary<SyntaxAnnotation, (ITypeSymbol? Type, int Position)> ConditionalTypes,
        HashSet<SyntaxAnnotation> DiscardedConditionals,
        Dictionary<SyntaxNode, List<SyntaxAnnotation>> ConditionalCalls,
        List<BindingEvidence> Evidence, List<DiagnosticEvidence> Diagnostics);

    private sealed record DiagnosticEvidence(
        DiagnosticSeverity Severity, string Id, string Message, DocumentId? Document,
        SyntaxAnnotation? Start, SyntaxAnnotation? End, int StartOffset, int EndOffset, string? ExternalIdentity,
        SyntaxTree? SourceTree, Microsoft.CodeAnalysis.Text.TextSpan SourceSpan);

    private sealed record BindingEvidence(
        SyntaxAnnotation Annotation, string Target, string TypeArguments,
        string? Type, string? ConvertedType, string Conversion, string? Parameter, SyntaxNode OriginalNode, ImmutableArray<SyntaxAnnotation> ContainingArguments,
        ImmutableArray<(string Parameter, object? Value)> CallerInfo = default);

    public static async Task<(Solution Accumulator, List<FileChangeDto> Changes, List<CallsiteUpdateDto> CallsiteUpdates, List<string> Warnings)> BuildAsync(
        Solution solution,
        IMethodSymbol method,
        Func<ParameterListSyntax, ParameterListSyntax> updateDeclaration,
        Func<BoundCallContext, SeparatedSyntaxList<ArgumentSyntax>, SeparatedSyntaxList<ArgumentSyntax>> updateCallsite,
        CancellationToken ct)
    {
        var related = await CollectRelatedSymbolsAsync(method, solution, ct).ConfigureAwait(false);
        var constructionPlans = new Dictionary<string, ChangeSignatureStructConstruction>(StringComparer.Ordinal);
        var constructionDeclarations = new Dictionary<string, ChangeSignatureStructConstruction>(StringComparer.Ordinal);
        var constructionOwners = new Dictionary<DocumentId, Dictionary<SyntaxNode, ChangeSignatureStructConstruction>>();
        var declarationKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new Dictionary<DocumentId, Dictionary<SyntaxNode, SyntaxAnnotation>>();
        foreach (var symbol in related)
        {
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                var declaration = await reference.GetSyntaxAsync(ct).ConfigureAwait(false);
                var list = declaration switch
                {
                    BaseMethodDeclarationSyntax ordinary => ordinary.ParameterList,
                    TypeDeclarationSyntax primary => primary.ParameterList,
                    _ => null,
                };
                if (list is null) continue;
                var document = solution.GetDocument(declaration.SyntaxTree);
                if (document is null) continue;
                var identity = SymbolIdentity(symbol.OriginalDefinition);
                var key = declarationKeys.TryGetValue(identity, out var existingKey)
                    ? existingKey : document.Id.Id.ToString("N") + ":" + declaration.SpanStart;
                declarationKeys[identity] = key;
                if (!declarations.TryGetValue(document.Id, out var entries))
                    declarations[document.Id] = entries = [];
                entries[declaration] = new SyntaxAnnotation(DeclarationTag, key);
                var construction = constructionPlans.TryGetValue(identity, out var existingConstruction)
                    ? existingConstruction : ChangeSignatureStructConstruction.Create(symbol, list, updateDeclaration(list));
                if (construction is not null)
                {
                    constructionPlans[identity] = construction;
                    constructionDeclarations[key] = construction;
                    var owner = declaration is TypeDeclarationSyntax ? declaration
                        : declaration.Ancestors().OfType<TypeDeclarationSyntax>().First();
                    if (!constructionOwners.TryGetValue(document.Id, out var owners))
                        constructionOwners[document.Id] = owners = [];
                    if (construction.EmitsAt(declaration)) owners.TryAdd(owner, construction);
                }
            }
        }
        if (declarations.Count == 0) throw Refuse("The target has no editable source declaration.");
        var affectedProjects = declarations.Keys.Select(id => id.ProjectId).ToHashSet();
        foreach (var id in affectedProjects.ToArray())
            affectedProjects.UnionWith(solution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(id));


        var originalDiagnostics = new Dictionary<ProjectId, ImmutableArray<Diagnostic>>();
        var diagnosticEvidence = new Dictionary<ProjectId, List<DiagnosticEvidence>>();
        foreach (var projectId in affectedProjects)
        {
            var compilation = await solution.GetProject(projectId)!.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) throw Refuse("An original dependent project cannot be compiled for diagnostic verification.");
            var diagnostics = compilation.GetDiagnostics(ct)
                .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).ToImmutableArray();
            originalDiagnostics[projectId] = diagnostics;
            diagnosticEvidence[projectId] = diagnostics
                .Where(d => !d.Location.IsInSource || solution.GetDocument(d.Location.SourceTree!) is null)
                .Select(d => new DiagnosticEvidence(d.Severity, d.Id, d.GetMessage(CultureInfo.InvariantCulture),
                    null, null, null, 0, 0, ExternalDiagnosticIdentity(d), null, default)).ToList();
        }

        var candidate = solution;
        var evidence = new Dictionary<DocumentId, List<BindingEvidence>>();
        var changedDocuments = new HashSet<DocumentId>();
        var callCounts = new Dictionary<DocumentId, int>();
        var globalAliases = new HashSet<string>(StringComparer.Ordinal);
        async Task<DocumentScan?> ReadDocumentScanAsync(Document document, string[] sourceAliases, string? projectAlias)
        {
            ct.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (root is null || model is null) return null;

            Func<IMethodSymbol, string> getSymbolIdentity = new DocumentMethodIdentities().Get;
            var documentDiagnostics = new List<DiagnosticEvidence>();
            var diagnosticAnnotations = new Dictionary<SyntaxToken, List<SyntaxAnnotation>>();
            foreach (var diagnostic in originalDiagnostics[document.Project.Id].Where(d => d.Location.SourceTree == model.SyntaxTree))
            {
                var span = diagnostic.Location.SourceSpan;
                var first = root.FindToken(span.Start, findInsideTrivia: true);
                var last = root.FindToken(span.Length == 0 ? span.Start : span.End - 1, findInsideTrivia: true);
                var start = new SyntaxAnnotation(DiagnosticTag);
                var end = new SyntaxAnnotation(DiagnosticTag);
                AddTokenAnnotation(diagnosticAnnotations, first, start);
                AddTokenAnnotation(diagnosticAnnotations, last, end);
                documentDiagnostics.Add(new DiagnosticEvidence(diagnostic.Severity, diagnostic.Id,
                    diagnostic.GetMessage(CultureInfo.InvariantCulture), document.Id, start, end,
                    span.Start - first.SpanStart, span.End - last.SpanStart, null, model.SyntaxTree, span));
            }

            var annotations = new Dictionary<SyntaxNode, List<SyntaxAnnotation>>();
            foreach (var scope in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
                AddAnnotation(annotations, scope, new SyntaxAnnotation(AliasScopeTag, scope.SpanStart.ToString(CultureInfo.InvariantCulture)));
            var calls = new Dictionary<SyntaxNode, BoundCallContext>();
            // Context constructors only store this owned set; serial apply fills it before use.
            var requiredAliases = new HashSet<string>(StringComparer.Ordinal);
            var localProjectAlias = projectAlias ?? "@project|" + document.Project.Id.Id.ToString("N") + "|"
                + EncodeAliasPath(Path.GetDirectoryName(document.FilePath)!);
            var conditionalTypes = new Dictionary<SyntaxAnnotation, (ITypeSymbol? Type, int Position)>();
            var discardedConditionals = new HashSet<SyntaxAnnotation>();
            var conditionalCalls = new Dictionary<SyntaxNode, List<SyntaxAnnotation>>();
            var documentEvidence = new List<BindingEvidence>();
            if (declarations.TryGetValue(document.Id, out var ownDeclarations))
                foreach (var (node, annotation) in ownDeclarations) AddAnnotation(annotations, node, annotation);
            if (constructionOwners.TryGetValue(document.Id, out var ownConstructionOwners))
            {
                foreach (var (node, plan) in ownConstructionOwners) AddAnnotation(annotations, node, plan.Annotation);
                foreach (var selector in ownConstructionOwners.Values.Distinct().SelectMany(plan => plan.OriginalOnlyAttributes)
                    .Where(attribute => attribute.SyntaxTree == root.SyntaxTree))
                    AddAnnotation(annotations, selector, new SyntaxAnnotation(ChangeSignatureStructConstruction.OriginalOnlyAttributeTag));
            }

            // Bound scans cover constructors and primary-base calls that FindCallers omits.
            foreach (var node in root.DescendantNodes())
            {
                if (GetArguments(node) is not { } list)
                {
                    if (node is ExpressionSyntax referenceExpression
                        && model.GetSymbolInfo(referenceExpression, ct).Symbol is IMethodSymbol referenceTarget)
                    {
                        var referenceAnnotation = new SyntaxAnnotation(BindingTag);
                        AddAnnotation(annotations, node, referenceAnnotation);
                        var referenceEvidence = CreateEvidence(referenceAnnotation, referenceTarget, node, model, declarationKeys, getSymbolIdentity);
                        documentEvidence.Add(referenceEvidence);
                    }
                    continue;
                }
                var symbolInfo = model.GetSymbolInfo(node, ct);
                var target = symbolInfo.Symbol as IMethodSymbol;
                var originalOperation = model.GetOperation(node, ct);
                if (originalOperation is IDynamicInvocationOperation
                    && node is InvocationExpressionSyntax dynamicCall)
                {
                    var name = dynamicCall.Expression switch
                    {
                        MemberAccessExpressionSyntax dynamicMember => dynamicMember.Name.Identifier.ValueText,
                        MemberBindingExpressionSyntax dynamicMember => dynamicMember.Name.Identifier.ValueText,
                        SimpleNameSyntax simple => simple.Identifier.ValueText,
                        _ => null,
                    };
                    var receiver = dynamicCall.Expression is MemberAccessExpressionSyntax access
                        ? model.GetTypeInfo(access.Expression, ct).Type : null;
                    var possible = related.Any(m => m.Name == name);
                    if (possible && receiver is { TypeKind: not TypeKind.Dynamic })
                        possible = model.LookupSymbols(node.SpanStart, receiver, name)
                            .OfType<IMethodSymbol>().Any(m => declarationKeys.ContainsKey(getSymbolIdentity(CanonicalDefinition(m))));
                    if (possible && dynamicCall.Expression is SimpleNameSyntax)
                        possible = model.LookupSymbols(node.SpanStart, name: name).OfType<IMethodSymbol>()
                            .Any(m => declarationKeys.ContainsKey(getSymbolIdentity(CanonicalDefinition(m))));
                    if (possible)
                        throw Refuse("A dynamic invocation may bind to the edited member at runtime. Use a statically bound call before changing this signature.");
                }
                if (originalOperation is IDynamicObjectCreationOperation dynamicCreation
                    && related.Any(m => m.MethodKind == MethodKind.Constructor
                        && SymbolEqualityComparer.Default.Equals(m.ContainingType.OriginalDefinition,
                            (dynamicCreation.Type as INamedTypeSymbol)?.OriginalDefinition)))
                    throw Refuse("A dynamic constructor invocation may bind to the edited constructor at runtime. Use a statically bound construction before changing this signature.");
                if (target is null)
                {
                    if (symbolInfo.CandidateSymbols.OfType<IMethodSymbol>()
                        .Any(m => declarationKeys.ContainsKey(getSymbolIdentity(CanonicalDefinition(m)))))
                        throw Refuse("An unresolved invocation may bind to the edited member. Resolve its binding errors before changing this signature.");
                    continue;
                }
                var annotation = new SyntaxAnnotation(BindingTag);
                AddAnnotation(annotations, node, annotation);
                var binding = CreateEvidence(annotation, target, node, model, declarationKeys, getSymbolIdentity);
                var operation = originalOperation;
                var arguments = ArgumentsOf(operation);
                if (!declarationKeys.ContainsKey(getSymbolIdentity(CanonicalDefinition(target))))
                {
                    // Unrelated calls are never rewritten here. Their binding stays verified, and their
                    // compiler-supplied caller-info values are recorded so drift can be frozen afterwards.
                    var callerInfo = CallerInfoValues(arguments);
                    documentEvidence.Add(callerInfo.IsDefaultOrEmpty ? binding : binding with { CallerInfo = callerInfo });
                    continue;
                }
                documentEvidence.Add(binding);
                var operationTarget = operation is IInvocationOperation boundInvocation
                    ? boundInvocation.TargetMethod : target;
                calls[node] = new BoundCallContext(operationTarget, arguments, model, list.Arguments, node.SpanStart, requiredAliases);
                foreach (var conditional in node.Ancestors().OfType<ConditionalAccessExpressionSyntax>())
                {
                    var tag = annotations.TryGetValue(conditional, out var existing)
                        ? existing.FirstOrDefault(a => a.Kind == ConditionalTag) : null;
                    if (tag is null)
                    {
                        tag = new SyntaxAnnotation(ConditionalTag);
                        AddAnnotation(annotations, conditional, tag);
                        conditionalTypes[tag] = (model.GetTypeInfo(conditional, ct).Type, conditional.SpanStart);
                        if (conditional.Parent is LambdaExpressionSyntax lambda
                            && model.GetTypeInfo(lambda, ct).ConvertedType is INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: true })
                            discardedConditionals.Add(tag);
                    }
                    if (!conditionalCalls.TryGetValue(node, out var tags)) conditionalCalls[node] = tags = [];
                    tags.Add(tag);
                }
                if (target.ReducedFrom is not null && node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member })
                {
                    var receiverAnnotation = new SyntaxAnnotation(ArgumentTag);
                    AddAnnotation(annotations, member.Expression, receiverAnnotation);
                    documentEvidence.Add(CreateEvidence(receiverAnnotation, operationTarget, member.Expression, model, declarationKeys, getSymbolIdentity)
                        with
                    { Parameter = operationTarget.Parameters[0].Name });
                }
                foreach (var argument in list.Arguments)
                {
                    var argAnnotation = new SyntaxAnnotation(ArgumentTag);
                    AddAnnotation(annotations, argument.Expression, argAnnotation);
                    var owner = ArgumentOperationResolver.Resolve(model, argument.Expression, ct);
                    documentEvidence.Add(CreateEvidence(argAnnotation, operationTarget, argument.Expression, model,
                        declarationKeys, getSymbolIdentity) with
                    { Parameter = owner?.Parameter?.Name });
                }
            }
            return new DocumentScan(document, root, model, diagnosticAnnotations, annotations, calls, requiredAliases,
                sourceAliases, localProjectAlias, conditionalTypes, discardedConditionals, conditionalCalls,
                documentEvidence, documentDiagnostics);
        }

        void ApplyDocumentScan(DocumentScan scan)
        {
            var (document, root, model, diagnosticAnnotations, annotations, calls, requiredAliases,
                sourceAliases, localProjectAlias, conditionalTypes, discardedConditionals, conditionalCalls,
                documentEvidence, documentDiagnostics) = scan;
            diagnosticEvidence[document.Project.Id].AddRange(documentDiagnostics);
            requiredAliases.UnionWith(globalAliases);
            requiredAliases.UnionWith(sourceAliases);
            requiredAliases.Add(localProjectAlias);
            if (annotations.Count == 0 && diagnosticAnnotations.Count == 0) return;
            var marked = AttachAnnotations(root, annotations, diagnosticAnnotations);
            var replacements = new Dictionary<SyntaxAnnotation, BoundCallContext>();
            foreach (var (node, context) in calls)
                replacements[annotations[node].Single(a => a.Kind == BindingTag)] = context;
            var rewriteNodes = marked.GetAnnotatedNodes(DeclarationTag)
                .Concat(marked.GetAnnotatedNodes(ChangeSignatureStructConstruction.OwnerTag))
                .Concat(marked.GetAnnotatedNodes(BindingTag).Where(n => n.GetAnnotations(BindingTag).Any(replacements.ContainsKey)))
                .Distinct()
                .ToArray();
            var affectedConditionals = new HashSet<SyntaxAnnotation>();
            var count = 0;
            var rewrittenRoot = marked.ReplaceNodes(rewriteNodes, (original, rewritten) =>
            {
                if (original.HasAnnotations(DeclarationTag))
                {
                    rewritten = rewritten switch
                    {
                        ConstructorDeclarationSyntax declaration when original.GetAnnotations(DeclarationTag).FirstOrDefault()?.Data is { } key
                            && constructionDeclarations.TryGetValue(key, out var plan)
                            => plan.RewriteConstructor(declaration, updateDeclaration(declaration.ParameterList)),
                        BaseMethodDeclarationSyntax declaration => declaration.WithParameterList(updateDeclaration(declaration.ParameterList)),
                        TypeDeclarationSyntax { ParameterList: { } primaryList } declaration
                            when constructionOwners.TryGetValue(document.Id, out var owners)
                                && owners.Values.SingleOrDefault(plan => original.HasAnnotation(plan.Annotation)) is { } plan
                            => plan.RewritePrimary(declaration, primaryList, updateDeclaration(primaryList), DeclarationTag),
                        TypeDeclarationSyntax { ParameterList: { } primaryList } declaration => declaration.WithParameterList(updateDeclaration(primaryList)),
                        _ => rewritten,
                    };
                }
                var callAnnotation = original.GetAnnotations(BindingTag).FirstOrDefault(replacements.ContainsKey);
                if (callAnnotation is not null && GetArguments(rewritten) is { } list)
                {
                    var context = replacements[callAnnotation];
                    var updated = updateCallsite(context, list.Arguments);
                    if (constructionPlans.TryGetValue(context.TargetIdentity, out var construction))
                        updated = construction.Arguments(updated, context.ContainingTypeName());
                    if (!updated.Equals(list.Arguments)) count++;
                    rewritten = WithArguments(rewritten, list.WithArguments(updated));
                    if (rewritten is InvocationExpressionSyntax genericInvocation)
                        rewritten = genericInvocation.WithExpression(context.RetainTypeSubstitution(genericInvocation.Expression));
                    if (context.RemovesExtensionReceiver && rewritten is InvocationExpressionSyntax invocation)
                    {
                        rewritten = invocation.WithExpression(context.StaticExtensionTarget(invocation.Expression));
                        var sourceCall = calls.Keys.Single(n => annotations[n].Contains(callAnnotation));
                        if (conditionalCalls.TryGetValue(sourceCall, out var tags)) affectedConditionals.UnionWith(tags);
                    }
                }
                if (rewritten is TypeDeclarationSyntax owner
                    && constructionOwners.TryGetValue(document.Id, out var plans)
                    && plans.Values.SingleOrDefault(plan => original.HasAnnotation(plan.Annotation)) is { } constructionPlan)
                    rewritten = constructionPlan.Complete(owner, DeclarationTag);
                return rewritten;
            });
            if (affectedConditionals.Count > 0)
                rewrittenRoot = new ConditionalReceiverRewriter(affectedConditionals, conditionalTypes, discardedConditionals, root, model, requiredAliases).Visit(rewrittenRoot)!;
            var requestedAliases = requiredAliases.Where(alias => !alias.StartsWith("@source|", StringComparison.Ordinal)
                && !alias.StartsWith("@project|", StringComparison.Ordinal)).ToArray();
            globalAliases.UnionWith(requestedAliases.Where(a => a.StartsWith("@global|", StringComparison.Ordinal) || a.StartsWith("@newglobal|", StringComparison.Ordinal)));
            if (requestedAliases.Length > 0) rewrittenRoot = AddAliasDirectives(rewrittenRoot, requestedAliases);
            documentEvidence = documentEvidence.Select(entry => entry with
            {
                ContainingArguments = entry.OriginalNode.AncestorsAndSelf()
                    .SelectMany(node => annotations.TryGetValue(node, out var values) ? values : [])
                    .Where(a => a.Kind == ArgumentTag).ToImmutableArray(),
            }).ToList();
            var textChanged = !string.Equals(root.ToFullString(), rewrittenRoot.ToFullString(), StringComparison.Ordinal);
            // Keep annotations until both late alias passes have finished. Only real edits
            // require reparsing; an initially untouched alias host may still be edited later.
            if (textChanged) rewrittenRoot = RoundTripRoot(rewrittenRoot, document, ct);
            candidate = candidate.WithDocumentSyntaxRoot(document.Id, rewrittenRoot);
            evidence[document.Id] = documentEvidence;
            if (textChanged)
                changedDocuments.Add(document.Id);
            if (count > 0) callCounts[document.Id] = count;
        }

        foreach (var project in solution.Projects.Where(p => affectedProjects.Contains(p.Id)))
        {
            var sourceAliases = project.Documents.Where(document => document.FilePath is not null)
                .Select(document => "@source|" + document.Id.Id.ToString("N") + "|" + EncodeAliasPath(document.FilePath!)).ToArray();
            var projectAlias = project.FilePath is null ? null
                : "@project|" + project.Id.Id.ToString("N") + "|" + EncodeAliasPath(Path.GetDirectoryName(project.FilePath)!);
            var documents = project.Documents.ToArray();
            if (documents.Length == 0) continue;
            ct.ThrowIfCancellationRequested();
            var outcomes = await ReadBatchAsync(documents,
                document => ReadDocumentScanAsync(document, sourceAliases, projectAlias)).ConfigureAwait(false);
            ConsumeReadOutcomes(outcomes, scan =>
            {
                if (scan is not null) ApplyDocumentScan(scan);
            }, ct);
        }

        foreach (var group in globalAliases.Where(a => a.StartsWith("@global|", StringComparison.Ordinal)).GroupBy(a => a.Split('|')[1], StringComparer.Ordinal))
        {
            var originalDocument = solution.Projects.SelectMany(p => p.Documents).Single(d => d.Id.Id.ToString("N") == group.Key);
            var document = candidate.GetDocument(originalDocument.Id)!;
            var root = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
            var directives = group.SelectMany(a =>
            {
                var parts = a.Split('|');
                var entries = new List<string> { "@globalusing:" + parts[2] + ":" + parts[3] };
                if (!((CompilationUnitSyntax)root).Externs.Any(e => e.Identifier.ValueText == parts[3])) entries.Add(parts[3]);
                return entries;
            }).Distinct(StringComparer.Ordinal);
            var updated = RoundTripRoot(AddAliasDirectivesToScope(root, directives), document, ct);
            candidate = candidate.WithDocumentSyntaxRoot(document.Id, updated);
            changedDocuments.Add(document.Id);
        }

        foreach (var plan in globalAliases.Where(a => a.StartsWith("@newglobal|", StringComparison.Ordinal)))
        {
            var parts = plan.Split('|');
            var projectId = solution.ProjectIds.Single(id => id.Id.ToString("N") == parts[1]);
            var documentId = DocumentId.CreateFromSerialized(projectId, Guid.ParseExact(parts[2], "N"));
            var text = Microsoft.CodeAnalysis.Text.SourceText.From("extern alias " + EscapeIdentifier(parts[4])
                + "; global using " + EscapeIdentifier(parts[3]) + " = " + EscapeIdentifier(parts[4]) + "; ", System.Text.Encoding.UTF8);
            candidate = candidate.AddDocument(documentId, Path.GetFileName(DecodeAliasPath(parts[5])), text, filePath: DecodeAliasPath(parts[5]));
            changedDocuments.Add(documentId);
        }

        candidate = await RestoreUnchangedDocumentTreesAsync(solution, candidate, affectedProjects, changedDocuments, ct).ConfigureAwait(false);
        candidate = await FreezeDriftedCallerInfoAsync(candidate, evidence, changedDocuments, ct).ConfigureAwait(false);
        var warnings = await ValidateCandidateAsync(solution, candidate, affectedProjects, evidence, declarationKeys, diagnosticEvidence, ct).ConfigureAwait(false);
        // Drop verification-only annotations and unchanged document versions from the public preview.
        var accumulator = solution;
        var changes = new List<FileChangeDto>();
        foreach (var id in changedDocuments)
        {
            var original = solution.GetDocument(id);
            var updated = candidate.GetDocument(id)!;
            var oldText = original is null ? Microsoft.CodeAnalysis.Text.SourceText.From(string.Empty)
                : await original.GetTextAsync(ct).ConfigureAwait(false);
            var newText = await updated.GetTextAsync(ct).ConfigureAwait(false);
            accumulator = original is null
                ? accumulator.AddDocument(id, updated.Name, newText, updated.Folders, updated.FilePath)
                : accumulator.WithDocumentText(id, newText);
            var path = updated.FilePath ?? updated.Name;
            changes.Add(new FileChangeDto(path, DiffGenerator.GenerateUnifiedDiff(oldText.ToString(), newText.ToString(), path)));
        }
        var updates = callCounts.Select(pair => new CallsiteUpdateDto(
                solution.GetDocument(pair.Key)?.FilePath ?? solution.GetDocument(pair.Key)!.Name, pair.Value))
            .OrderBy(u => u.FilePath, StringComparer.Ordinal).ToList();
        return (accumulator, changes, updates, warnings);
    }



    // Lower only conditional accesses affected by removing their extension receiver.
    // Receiver tests stay at their original evaluation position, including loop iterators.
    private sealed class ConditionalReceiverRewriter(
        HashSet<SyntaxAnnotation> affected, Dictionary<SyntaxAnnotation, (ITypeSymbol? Type, int Position)> types,
        HashSet<SyntaxAnnotation> discarded, SyntaxNode original, SemanticModel model, HashSet<string> requiredAliases) : CSharpSyntaxRewriter
    {
        private readonly HashSet<string> _names = original.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken))
            .Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        private string Fresh(string prefix)
        {
            var index = 0;
            while (!_names.Add(prefix + index)) index++;
            return prefix + index;
        }

        private bool IsAffected(ConditionalAccessExpressionSyntax node) =>
            node.GetAnnotations(ConditionalTag).Any(affected.Contains);

        private (ExpressionSyntax Condition, ExpressionSyntax Body) Parts(ConditionalAccessExpressionSyntax node)
        {
            // Bind only member/element bindings owned by this conditional access.
            var bindings = node.WhenNotNull.DescendantNodesAndSelf().Where(n =>
                n is MemberBindingExpressionSyntax or ElementBindingExpressionSyntax
                && n.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
                    .FirstOrDefault(owner => owner.WhenNotNull.FullSpan.Contains(n.FullSpan)) == node).ToArray();
            var receiver = SyntaxFactory.ParenthesizedExpression(node.Expression);
            ExpressionSyntax condition;
            var body = node.WhenNotNull;
            if (bindings.Length == 0)
                condition = SyntaxFactory.IsPatternExpression(receiver,
                    SyntaxFactory.UnaryPattern(SyntaxFactory.Token(SyntaxKind.NotKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                        SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
            else
            {
                var name = Fresh("__signatureReceiver");
                condition = SyntaxFactory.IsPatternExpression(receiver,
                    SyntaxFactory.RecursivePattern().WithPropertyPatternClause(SyntaxFactory.PropertyPatternClause())
                        .WithDesignation(SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(name)))
                        .WithTrailingTrivia(SyntaxFactory.Space));
                body = body.ReplaceNodes(bindings, (old, rewritten) => old switch
                {
                    MemberBindingExpressionSyntax member => member.CopyAnnotationsTo(SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(name), member.OperatorToken, member.Name)),
                    ElementBindingExpressionSyntax element => element.CopyAnnotationsTo(SyntaxFactory.ElementAccessExpression(
                        SyntaxFactory.IdentifierName(name), element.ArgumentList)),
                    _ => rewritten,
                });
            }
            condition = ((IsPatternExpressionSyntax)condition).WithIsKeyword(SyntaxFactory.Token(SyntaxKind.IsKeyword).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space));
            return (condition, body);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var updated = (ConditionalAccessExpressionSyntax)base.VisitConditionalAccessExpression(node)!;
            if (!IsAffected(updated)) return updated;
            var (type, position) = types[updated.GetAnnotations(ConditionalTag).First(affected.Contains)];
            // Statement contexts are lowered by their owning statement/arrow/loop.
            if (type?.SpecialType == SpecialType.System_Void || IsDiscarded(node)) return updated;
            var (condition, body) = Parts(updated);
            var syntax = type is null ? null : SyntaxFactory.ParseTypeName(MaterializedTypeName(type, model, position, requiredAliases));
            ExpressionSyntax absent = syntax is null || syntax.ContainsDiagnostics
                ? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression) : SyntaxFactory.DefaultExpression(syntax);
            if (syntax is not null && !syntax.ContainsDiagnostics)
                body = SyntaxFactory.CastExpression(syntax, SyntaxFactory.ParenthesizedExpression(body));
            return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ConditionalExpression(condition, body, absent)
                .WithQuestionToken(updated.OperatorToken));
        }

        private bool IsDiscarded(ConditionalAccessExpressionSyntax node) =>
            node.GetAnnotations(ConditionalTag).Any(discarded.Contains) || node.Parent is ExpressionStatementSyntax or ForStatementSyntax
            || node.Parent is ArrowExpressionClauseSyntax arrow && arrow.Parent switch
            {
                MethodDeclarationSyntax method => method.ReturnType is PredefinedTypeSyntax t && t.Keyword.IsKind(SyntaxKind.VoidKeyword),
                LocalFunctionStatementSyntax method => method.ReturnType is PredefinedTypeSyntax t && t.Keyword.IsKind(SyntaxKind.VoidKeyword),
                ConstructorDeclarationSyntax or DestructorDeclarationSyntax => true,
                AccessorDeclarationSyntax accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration),
                _ => false,
            };

        private StatementSyntax Statement(ExpressionSyntax expression, SyntaxToken semicolon)
        {
            if (expression is ConditionalAccessExpressionSyntax conditional && IsAffected(conditional))
            {
                var (condition, body) = Parts(conditional);
                return SyntaxFactory.IfStatement(condition, Statement(body, semicolon))
                    .WithCloseParenToken(SyntaxFactory.Token(SyntaxKind.CloseParenToken)
                        .WithTrailingTrivia(conditional.OperatorToken.LeadingTrivia.AddRange(conditional.OperatorToken.TrailingTrivia)));
            }
            return SyntaxFactory.ExpressionStatement(expression).WithSemicolonToken(semicolon);
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            var updated = (ExpressionStatementSyntax)base.VisitExpressionStatement(node)!;
            return Statement(updated.Expression, updated.SemicolonToken);
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var updated = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (updated.ExpressionBody?.Expression is ConditionalAccessExpressionSyntax conditional && IsAffected(conditional))
                return updated.WithExpressionBody(null).WithSemicolonToken(default)
                    .WithBody(ArrowBody(updated.ExpressionBody, updated.SemicolonToken));
            return updated;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var updated = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (updated.ExpressionBody?.Expression is ConditionalAccessExpressionSyntax conditional && IsAffected(conditional))
                return updated.WithExpressionBody(null).WithSemicolonToken(default)
                    .WithBody(ArrowBody(updated.ExpressionBody, updated.SemicolonToken));
            return updated;
        }

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            var updated = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node)!;
            if (updated.ExpressionBody?.Expression is ConditionalAccessExpressionSyntax conditional && IsAffected(conditional))
                return updated.WithExpressionBody(null).WithSemicolonToken(default)
                    .WithBody(ArrowBody(updated.ExpressionBody, updated.SemicolonToken));
            return updated;
        }


        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var updated = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
            return updated.ExpressionBody?.Expression is ConditionalAccessExpressionSyntax c && IsAffected(c)
                ? updated.WithBody(ArrowBody(updated.ExpressionBody, updated.SemicolonToken)).WithExpressionBody(null).WithSemicolonToken(default) : updated;
        }

        public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            var updated = (DestructorDeclarationSyntax)base.VisitDestructorDeclaration(node)!;
            return updated.ExpressionBody?.Expression is ConditionalAccessExpressionSyntax c && IsAffected(c)
                ? updated.WithBody(ArrowBody(updated.ExpressionBody, updated.SemicolonToken)).WithExpressionBody(null).WithSemicolonToken(default) : updated;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            var updated = (SimpleLambdaExpressionSyntax)base.VisitSimpleLambdaExpression(node)!;
            return updated.Body is ConditionalAccessExpressionSyntax c && IsAffected(c)
                ? updated.WithBody(SyntaxFactory.Block(Statement(c, SyntaxFactory.Token(SyntaxKind.SemicolonToken)))) : updated;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var updated = (ParenthesizedLambdaExpressionSyntax)base.VisitParenthesizedLambdaExpression(node)!;
            return updated.Body is ConditionalAccessExpressionSyntax c && IsAffected(c)
                ? updated.WithBody(SyntaxFactory.Block(Statement(c, SyntaxFactory.Token(SyntaxKind.SemicolonToken)))) : updated;
        }

        private BlockSyntax ArrowBody(ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon) =>
            SyntaxFactory.Block(Statement(arrow.Expression, semicolon))
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia).WithTrailingTrivia(arrow.ArrowToken.TrailingTrivia));


        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var updated = (ForStatementSyntax)base.VisitForStatement(node)!;
            if (!updated.Initializers.Concat(updated.Incrementors).OfType<ConditionalAccessExpressionSyntax>().Any(IsAffected)) return updated;
            return LowerForExpressions(updated, Statement, Fresh);
        }

    }

    private static StatementSyntax RouteContinues(StatementSyntax body, string label)
    {
        var continues = body.DescendantNodesAndSelf().OfType<ContinueStatementSyntax>()
            .Where(c => !c.Ancestors().TakeWhile(a => a != body.Parent).Any(a => a is
                ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax or WhileStatementSyntax
                or DoStatementSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).ToArray();
        return body.ReplaceNodes(continues, (old, _) =>
            SyntaxFactory.GotoStatement(SyntaxKind.GotoStatement, SyntaxFactory.IdentifierName(label))
                .WithGotoKeyword(SyntaxFactory.Token(SyntaxKind.GotoKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                .WithSemicolonToken(old.SemicolonToken).WithTriviaFrom(old));
    }

    private static StatementSyntax LowerForExpressions(ForStatementSyntax updated,
        Func<ExpressionSyntax, SyntaxToken, StatementSyntax> statement, Func<string, string> fresh)
    {
        StatementSyntax[] LowerExpressions(SeparatedSyntaxList<ExpressionSyntax> expressions) =>
            expressions.Select((expression, index) =>
            {
                var token = SyntaxFactory.Token(SyntaxKind.SemicolonToken);
                if (index < expressions.SeparatorCount)
                {
                    var comma = expressions.GetSeparator(index);
                    token = token.WithLeadingTrivia(comma.LeadingTrivia).WithTrailingTrivia(comma.TrailingTrivia);
                }
                return statement(expression, token);
            }).ToArray();

        var initializers = LowerExpressions(updated.Initializers);
        var incrementors = LowerExpressions(updated.Incrementors);
        var body = updated.Statement;
        if (incrementors.Length > 0)
        {
            var label = fresh("__signatureIterator");
            body = RouteContinues(body, label);
            body = SyntaxFactory.Block(new[] { body, SyntaxFactory.LabeledStatement(label, SyntaxFactory.EmptyStatement()) }.Concat(incrementors));
        }
        var hadInitializers = updated.Initializers.Count > 0;
        updated = updated.WithStatement(body).WithInitializers(default).WithIncrementors(default);
        if (!hadInitializers) return updated;
        return SyntaxFactory.Block(initializers.Append(updated));
    }

    internal static ConstructorDeclarationSyntax PrepareGeneratedConstructor(ConstructorDeclarationSyntax constructor)
    {
        var identity = constructor.GetAnnotations(DeclarationTag).ToArray();
        var visited = (ConstructorDeclarationSyntax)new GeneratedConstructorCleanupRewriter().Visit(constructor)!;
        var cleaned = constructor.ReplaceNode(constructor, visited);
        return cleaned.WithAdditionalAnnotations(identity).NormalizeWhitespace(eol: " ")
            .WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space);
    }

    private static async Task<Solution> RestoreUnchangedDocumentTreesAsync(
        Solution original, Solution candidate, HashSet<ProjectId> affectedProjects,
        HashSet<DocumentId> changedDocuments, CancellationToken ct)
    {
        foreach (var document in original.Projects.Where(project => affectedProjects.Contains(project.Id)).SelectMany(project => project.Documents))
        {
            ct.ThrowIfCancellationRequested();
            if (changedDocuments.Contains(document.Id)) continue;
            var originalRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var candidateRoot = await candidate.GetDocument(document.Id)!.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (originalRoot is null || candidateRoot is null) continue;
            if (!string.Equals(originalRoot.ToFullString(), candidateRoot.ToFullString(), StringComparison.Ordinal))
                throw Refuse("The candidate text does not preserve its verified syntax identities.");
        }
        // Root replacement creates another SyntaxTree even with PreserveIdentity. Begin
        // with the immutable original document states, then overlay only actual edits in
        // candidate order so new alias documents retain their order as well as their data.
        var restored = original;
        foreach (var document in candidate.Projects.SelectMany(project => project.Documents))
        {
            ct.ThrowIfCancellationRequested();
            if (!changedDocuments.Contains(document.Id)) continue;
            if (original.GetDocument(document.Id) is not null)
            {
                var root = (await document.GetSyntaxRootAsync(ct).ConfigureAwait(false))!;
                restored = restored.WithDocumentSyntaxRoot(document.Id, root);
            }
            else
            {
                var text = await document.GetTextAsync(ct).ConfigureAwait(false);
                var version = await document.GetTextVersionAsync(ct).ConfigureAwait(false);
                restored = restored.AddDocument(DocumentInfo.Create(document.Id, document.Name,
                    folders: document.Folders, sourceCodeKind: document.SourceCodeKind,
                    loader: TextLoader.From(TextAndVersion.Create(text, version, document.FilePath)),
                    filePath: document.FilePath));
            }
        }
        return restored;
    }

    private static SyntaxNode RoundTripRoot(SyntaxNode root, Document document, CancellationToken ct)
    {
        var annotations = root.DescendantNodesAndSelf()
            .Where(n => n.HasAnnotations(DeclarationTag) || n.HasAnnotations(BindingTag) || n.HasAnnotations(ArgumentTag))
            .ToDictionary(n => (n.RawKind, n.Span), n => n.GetAnnotations(DeclarationTag)
                .Concat(n.GetAnnotations(BindingTag)).Concat(n.GetAnnotations(ArgumentTag)).ToArray());
        var tokenAnnotations = TokenIdentityKeys(root).Where(pair => pair.Token.HasAnnotations(DiagnosticTag))
            .ToDictionary(pair => pair.Key, pair => pair.Token.GetAnnotations(DiagnosticTag).ToArray());
        var parsed = CSharpSyntaxTree.ParseText(root.ToFullString(),
            document.Project.ParseOptions as CSharpParseOptions, document.FilePath ?? document.Name,
            cancellationToken: ct).GetRoot(ct);
        var nodes = parsed.DescendantNodesAndSelf().Where(n => annotations.ContainsKey((n.RawKind, n.Span))).ToArray();
        var tokens = TokenIdentityKeys(parsed).Where(pair => tokenAnnotations.ContainsKey(pair.Key)).ToArray();
        if (nodes.Length != annotations.Count || tokens.Length != tokenAnnotations.Count)
            throw Refuse("The candidate text does not preserve its verified syntax identities.");
        var nodeMap = nodes.ToDictionary(node => node, node => annotations[(node.RawKind, node.Span)].ToList());
        var tokenMap = tokens.ToDictionary(pair => pair.Token, pair => tokenAnnotations[pair.Key].ToList());
        return AttachAnnotations(parsed, nodeMap, tokenMap);
    }

    private static SyntaxNode AttachAnnotations(SyntaxNode root,
        Dictionary<SyntaxNode, List<SyntaxAnnotation>> nodes, Dictionary<SyntaxToken, List<SyntaxAnnotation>> tokens)
    {
        var visited = new AnnotationAttachmentRewriter(nodes, tokens).Visit(root)!;
        // ReplaceCore supplies the original tree's options even for a non-compilation-unit root.
        return root.ReplaceNode(root, visited);
    }

    private sealed class AnnotationAttachmentRewriter(
        Dictionary<SyntaxNode, List<SyntaxAnnotation>> nodes,
        Dictionary<SyntaxToken, List<SyntaxAnnotation>> tokens) : CSharpSyntaxRewriter(visitIntoStructuredTrivia: true)
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var rewritten = base.Visit(node);
            return node is not null && rewritten is not null && nodes.TryGetValue(node, out var annotations)
                ? rewritten.WithAdditionalAnnotations(annotations.ToArray()) : rewritten;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var rewritten = base.VisitToken(token);
            return tokens.TryGetValue(token, out var annotations)
                ? rewritten.WithAdditionalAnnotations(annotations.ToArray()) : rewritten;
        }
    }

    private sealed class GeneratedConstructorCleanupRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var rewritten = base.Visit(node);
            return rewritten?.WithoutAnnotations(new[] { DeclarationTag, BindingTag, ArgumentTag,
                ConditionalTag, AliasScopeTag, ChangeSignatureStructConstruction.OwnerTag,
                ChangeSignatureStructConstruction.OriginalOnlyAttributeTag }
                .SelectMany(kind => rewritten.GetAnnotations(kind)).ToArray());
        }

        public override SyntaxToken VisitToken(SyntaxToken token) =>
            base.VisitToken(token).WithoutAnnotations(DiagnosticTag).WithoutTrivia();
    }

    private static IEnumerable<((int Kind, Microsoft.CodeAnalysis.Text.TextSpan Span, int Occurrence) Key, SyntaxToken Token)> TokenIdentityKeys(SyntaxNode root)
    {
        var occurrences = new Dictionary<(int, Microsoft.CodeAnalysis.Text.TextSpan), int>();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            var key = (token.RawKind, token.Span);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            yield return ((token.RawKind, token.Span, occurrence), token);
        }
    }

    private static void AddTokenAnnotation(Dictionary<SyntaxToken, List<SyntaxAnnotation>> map, SyntaxToken token, SyntaxAnnotation annotation)
    {
        if (!map.TryGetValue(token, out var values)) map[token] = values = [];
        values.Add(annotation);
    }

    private static string ExternalDiagnosticIdentity(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource) return diagnostic.Location.Kind.ToString();
        var tree = diagnostic.Location.SourceTree!;
        return tree.FilePath + ":" + diagnostic.Location.SourceSpan + ":" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tree.ToString())));
    }

    private static void AddAnnotation(Dictionary<SyntaxNode, List<SyntaxAnnotation>> map, SyntaxNode node, SyntaxAnnotation annotation)
    {
        if (!map.TryGetValue(node, out var values)) map[node] = values = [];
        values.Add(annotation);
    }

    private static ArgumentListSyntax? GetArguments(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax invocation => invocation.ArgumentList,
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList ?? SyntaxFactory.ArgumentList(),
        ConstructorInitializerSyntax initializer => initializer.ArgumentList,
        PrimaryConstructorBaseTypeSyntax primaryBase => primaryBase.ArgumentList,
        _ => null,
    };

    private static SyntaxNode WithArguments(SyntaxNode node, ArgumentListSyntax list) => node switch
    {
        InvocationExpressionSyntax invocation => invocation.WithArgumentList(list),
        ObjectCreationExpressionSyntax creation => creation.WithArgumentList(list),
        ImplicitObjectCreationExpressionSyntax creation => creation.WithArgumentList(list),
        ConstructorInitializerSyntax initializer => initializer.WithArgumentList(list),
        PrimaryConstructorBaseTypeSyntax primaryBase => primaryBase.WithArgumentList(list),
        _ => throw Refuse("The bound call has no editable argument list."),
    };

    private static ImmutableArray<IArgumentOperation> FindArgumentOperations(IOperation? operation)
    {
        if (operation is null) return [];
        var result = ImmutableArray.CreateBuilder<IArgumentOperation>();
        foreach (var child in operation.ChildOperations)
            if (child is IArgumentOperation argument) result.Add(argument);
        return result.ToImmutable();
    }

    private static ImmutableArray<IArgumentOperation> ArgumentsOf(IOperation? operation) => operation switch
    {
        IInvocationOperation invocation => invocation.Arguments,
        IObjectCreationOperation creation => creation.Arguments,
        _ => FindArgumentOperations(operation),
    };

    private static bool IsCallerInfoParameter(IParameterSymbol parameter) =>
        parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
            is "System.Runtime.CompilerServices.CallerLineNumberAttribute"
                or "System.Runtime.CompilerServices.CallerFilePathAttribute"
                or "System.Runtime.CompilerServices.CallerMemberNameAttribute"
                or "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute");

    // Compiler-supplied caller-info values of one call: only omitted arguments carry them.
    private static ImmutableArray<(string Parameter, object? Value)> CallerInfoValues(ImmutableArray<IArgumentOperation> arguments) =>
        [.. arguments.Where(argument => argument.ArgumentKind == ArgumentKind.DefaultValue
                && argument.Parameter is { } parameter && IsCallerInfoParameter(parameter)
                && argument.Value.ConstantValue is { HasValue: true, Value: not null })
            .Select(argument => (argument.Parameter!.Name, argument.Value.ConstantValue.Value))];

    // An edit can move a caller-info site (line shifts, changed argument text) in a call that does not bind to the
    // target. Compare the candidate with the recorded original values and name ONLY the drifted parameters.
    // Freezing a call can change an enclosing call's argument text, so repeat until nothing drifts. Every
    // (call, parameter) pair is attempted at most once, so the loop is bounded by the finite number of recorded
    // pairs even if a freeze were ever lost; ValidateCandidateAsync then judges whatever remains.
    private static async Task<Solution> FreezeDriftedCallerInfoAsync(
        Solution candidate, Dictionary<DocumentId, List<BindingEvidence>> evidence,
        HashSet<DocumentId> changedDocuments, CancellationToken ct)
    {
        var attempted = new HashSet<(SyntaxAnnotation Call, string Parameter)>();
        while (true)
        {
            var frozenAny = false;
            foreach (var (documentId, entries) in evidence)
            {
                if (!changedDocuments.Contains(documentId)) continue;
                var recorded = entries.Where(entry => !entry.CallerInfo.IsDefaultOrEmpty).ToArray();
                if (recorded.Length == 0) continue;
                var document = candidate.GetDocument(documentId)!;
                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
                if (root is null || model is null) continue;
                var nodes = root.DescendantNodes().Where(n => n.HasAnnotations(BindingTag))
                    .SelectMany(n => n.GetAnnotations(BindingTag).Select(a => (Annotation: a, Node: n)))
                    .ToDictionary(pair => pair.Annotation, pair => pair.Node);
                var freezes = new Dictionary<SyntaxNode, List<(string Parameter, object? Value)>>();
                foreach (var entry in recorded)
                {
                    // A call that left the candidate with its argument is judged by ValidateCandidateAsync.
                    if (!nodes.TryGetValue(entry.Annotation, out var node)) continue;
                    var arguments = ArgumentsOf(model.GetOperation(node, ct));
                    var drifted = entry.CallerInfo.Where(original => !attempted.Contains((entry.Annotation, original.Parameter))
                        && arguments.Any(argument =>
                            argument.ArgumentKind == ArgumentKind.DefaultValue && argument.Parameter?.Name == original.Parameter
                            && argument.Value.ConstantValue.HasValue
                            && !Equals(argument.Value.ConstantValue.Value, original.Value))).ToList();
                    attempted.UnionWith(drifted.Select(freeze => (entry.Annotation, freeze.Parameter)));
                    if (drifted.Count > 0) freezes[node] = drifted;
                }
                if (freezes.Count == 0) continue;
                var frozen = root.ReplaceNodes(freezes.Keys, (original, rewritten) =>
                {
                    var list = GetArguments(rewritten)!;
                    var added = list.Arguments.AddRange(freezes[original].Select((freeze, index) =>
                        NamedArgument(freeze.Parameter, SyntaxFactory.ParseExpression(freeze.Value is string text
                            ? SymbolDisplay.FormatLiteral(text, true) : string.Create(CultureInfo.InvariantCulture, $"{freeze.Value}")))
                            .WithLeadingTrivia(list.Arguments.Count + index == 0 ? default : SyntaxFactory.Space)));
                    return WithArguments(rewritten, list.WithArguments(added));
                });
                candidate = candidate.WithDocumentSyntaxRoot(documentId, RoundTripRoot(frozen, document, ct));
                frozenAny = true;
            }
            if (!frozenAny) return candidate;
        }
    }

    // ArgumentTag annotates the argument expression; any other tagged node is not an argument.
    private static IArgumentOperation? ResolveArgument(SemanticModel model, SyntaxNode node, CancellationToken ct) =>
        node is ExpressionSyntax expression ? ArgumentOperationResolver.Resolve(model, expression, ct) : null;

    private static ArgumentSyntax NamedArgument(string name, ExpressionSyntax expression) =>
        SyntaxFactory.Argument(expression).WithNameColon(SyntaxFactory.NameColon(
            SyntaxFactory.IdentifierName(EscapeIdentifier(name))).WithTrailingTrivia(SyntaxFactory.Space));

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static ExpressionSyntax ConstantExpression(object? value, ITypeSymbol type, SemanticModel model, int position, HashSet<string> requiredAliases)
    {
        if (value is null) return SyntaxFactory.ParseExpression("default(" +
            MaterializedTypeName(type, model, position, requiredAliases) + ")");
        var literal = value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char character => SymbolDisplay.FormatLiteral(character, true),
            bool flag => flag ? "true" : "false",
            float number when float.IsNaN(number) => "global::System.Single.NaN",
            float number when float.IsPositiveInfinity(number) => "global::System.Single.PositiveInfinity",
            float number when float.IsNegativeInfinity(number) => "global::System.Single.NegativeInfinity",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
            double number when double.IsNaN(number) => "global::System.Double.NaN",
            double number when double.IsPositiveInfinity(number) => "global::System.Double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "global::System.Double.NegativeInfinity",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? throw Refuse("The original optional argument value is not representable."),
        };
        return SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName(
            MaterializedTypeName(type, model, position, requiredAliases)),
            SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ParseExpression(literal)));
    }

    private static SyntaxNode AddAliasDirectives(SyntaxNode root, IEnumerable<string> aliases)
    {
        var scopes = aliases.Where(a => !a.StartsWith("@global|", StringComparison.Ordinal) && !a.StartsWith("@newglobal|", StringComparison.Ordinal))
            .GroupBy(a => a[..a.IndexOf(':')])
            .ToDictionary(g => g.Key, g => g.Select(a => a[(a.IndexOf(':') + 1)..]).ToArray(), StringComparer.Ordinal);
        var namespaces = root.GetAnnotatedNodes(AliasScopeTag).OfType<BaseNamespaceDeclarationSyntax>()
            .Where(n => scopes.ContainsKey(n.GetAnnotations(AliasScopeTag).Single().Data!)).ToArray();
        root = root.ReplaceNodes(namespaces, (original, rewritten) => (BaseNamespaceDeclarationSyntax)AddAliasDirectivesToScope(
            rewritten, scopes[original.GetAnnotations(AliasScopeTag).Single().Data!]));
        return scopes.TryGetValue("-1", out var global) ? AddAliasDirectivesToScope(root, global) : root;
    }

    private static SyntaxNode AddAliasDirectivesToScope(SyntaxNode scope, IEnumerable<string> aliases)
    {
        // Add no line breaks: unrelated caller-line values must retain their original lines.
        var entries = aliases.OrderBy(a => a, StringComparer.Ordinal).ToArray();
        var directives = entries.Where(a => !a.StartsWith("@using:", StringComparison.Ordinal) && !a.StartsWith("@globalusing:", StringComparison.Ordinal))
            .Select(a => SyntaxFactory.ParseCompilationUnit("extern alias " + EscapeIdentifier(a) + "; ").Externs.Single()).ToArray();
        var usings = entries.Where(a => a.StartsWith("@using:", StringComparison.Ordinal))
            .Select(a => a["@using:".Length..].Split(':'))
            .Select(a => SyntaxFactory.ParseCompilationUnit("using " + EscapeIdentifier(a[0]) + " = " + EscapeIdentifier(a[1]) + "; ").Usings.Single()).ToArray();
        var globalUsings = entries.Where(a => a.StartsWith("@globalusing:", StringComparison.Ordinal))
            .Select(a => a["@globalusing:".Length..].Split(':'))
            .Select(a => SyntaxFactory.ParseCompilationUnit("global using " + EscapeIdentifier(a[0]) + " = " + EscapeIdentifier(a[1]) + "; ").Usings.Single()).ToArray();
        var externs = scope is CompilationUnitSyntax root ? root.Externs : ((BaseNamespaceDeclarationSyntax)scope).Externs;
        var originalUsings = scope is CompilationUnitSyntax unit ? unit.Usings : ((BaseNamespaceDeclarationSyntax)scope).Usings;
        if (externs.Count == 0 && (directives.Length > 0 || originalUsings.Count == 0))
        {
            var first = scope is BaseNamespaceDeclarationSyntax ns
                ? ns.Usings.FirstOrDefault()?.GetFirstToken() ?? ns.Members.First().GetFirstToken() : scope.GetFirstToken();
            if (directives.Length > 0) directives[0] = directives[0].WithLeadingTrivia(first.LeadingTrivia);
            else if (globalUsings.Length > 0) globalUsings[0] = globalUsings[0].WithLeadingTrivia(first.LeadingTrivia);
            else usings[0] = usings[0].WithLeadingTrivia(first.LeadingTrivia);
            scope = scope.ReplaceToken(first, first.WithLeadingTrivia(default(SyntaxTriviaList)));
        }
        var combinedUsings = SyntaxFactory.List(originalUsings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.None))
            .Concat(globalUsings).Concat(originalUsings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None))).Concat(usings));
        return scope switch
        {
            CompilationUnitSyntax compilation => compilation.WithExterns(externs.AddRange(directives)).WithUsings(combinedUsings),
            FileScopedNamespaceDeclarationSyntax file => file.WithExterns(externs.AddRange(directives)).WithUsings(originalUsings.AddRange(usings)),
            _ => ((NamespaceDeclarationSyntax)scope).WithExterns(externs.AddRange(directives)).WithUsings(originalUsings.AddRange(usings)),
        };
    }

    private static string MaterializedTypeName(ITypeSymbol type, SemanticModel model, int position, HashSet<string> requiredAliases)
    {
        var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var parts = type.ToDisplayParts(format);
        var root = model.SyntaxTree.GetRoot();
        var scopes = root.FindToken(position).Parent!.AncestorsAndSelf()
            .Where(n => n is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax).ToArray();
        var externs = root.DescendantNodes().OfType<ExternAliasDirectiveSyntax>()
            .Where(a => scopes.Contains(a.Parent)).ToArray();
        bool IsAccessible(string alias, SyntaxNode owner) => !scopes.TakeWhile(s => s != owner).Append(owner)
            .SelectMany(s => s is CompilationUnitSyntax unit ? unit.Usings : ((BaseNamespaceDeclarationSyntax)s).Usings)
            .Any(u => u.Alias?.Name.Identifier.ValueText == alias);
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.ToString() == "global" && i + 1 < parts.Length && parts[i + 1].ToString() == "::")
            {
                var named = parts.Skip(i + 2).TakeWhile(p => p.ToString() != "<")
                    .Select(p => p.Symbol).OfType<INamedTypeSymbol>().FirstOrDefault();
                // Prefer the original global binding when that assembly is already accessible.
                if (named is not null && SymbolEqualityComparer.Default.Equals(named,
                    model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName(named.ToDisplayString(format)),
                        SpeculativeBindingOption.BindAsTypeOrNamespace).Type))
                {
                    text.Append("global");
                    continue;
                }
                var reference = named is null ? null : model.Compilation.References.FirstOrDefault(r =>
                    SymbolEqualityComparer.Default.Equals(model.Compilation.GetAssemblyOrModuleSymbol(r), named.ContainingAssembly));
                var alias = reference?.Properties.Aliases.FirstOrDefault(a => a != "global" &&
                    externs.Any(e => e.Identifier.ValueText == a && IsAccessible(a, e.Parent!)))
                    ?? reference?.Properties.Aliases.FirstOrDefault(a => a != "global");
                if (alias is not null)
                {
                    var existing = externs.FirstOrDefault(e => e.Identifier.ValueText == alias);
                    if (existing is not null && !IsAccessible(alias, existing.Parent!))
                    {
                        // Alias the extern assembly root in its original scope, before a nearer alias shadows it.
                        var scopeId = (existing.Parent is CompilationUnitSyntax ? -1 : existing.Parent!.SpanStart)
                            .ToString(CultureInfo.InvariantCulture);
                        var prefix = scopeId + ":@using:";
                        var planned = requiredAliases.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal)
                            && a.EndsWith(":" + alias, StringComparison.Ordinal));
                        if (planned is null)
                        {
                            var fresh = FreshAssemblyAlias(model.Compilation, requiredAliases);
                            planned = prefix + fresh + ":" + alias;
                            requiredAliases.Add(planned);
                        }
                        alias = planned[prefix.Length..planned.LastIndexOf(':')];
                    }
                    else if (existing is null)
                    {
                        if (!IsAccessible(alias, scopes.Last()))
                        {
                            // Preserve existing alias uses by importing a fresh assembly-root alias from a safe source scope.
                            var host = model.Compilation.SyntaxTrees.FirstOrDefault(t => t.GetRoot() is CompilationUnitSyntax unit
                                && requiredAliases.Any(a => a.StartsWith("@source|", StringComparison.Ordinal) && a.EndsWith("|" + EncodeAliasPath(t.FilePath), StringComparison.Ordinal))
                                && !unit.Usings.Any(u => u.Alias?.Name.Identifier.ValueText == alias)
                                && (t == model.SyntaxTree || unit.DescendantNodes().OfType<ExternAliasDirectiveSyntax>()
                                    .Any(e => e.Identifier.ValueText == alias)));
                            if (host is null)
                            {
                                // A directive-only source document provides a scope without changing existing aliases or project references.
                                var project = requiredAliases.Single(a => a.StartsWith("@project|", StringComparison.Ordinal)).Split('|');
                                var newPrefix = "@newglobal|" + project[1] + "|";
                                var newPlan = requiredAliases.FirstOrDefault(a => a.StartsWith(newPrefix, StringComparison.Ordinal) && a.Split('|')[4] == alias);
                                if (newPlan is null)
                                {
                                    var fresh = FreshAssemblyAlias(model.Compilation, requiredAliases);
                                    var documentId = Guid.NewGuid().ToString("N");
                                    var path = Path.Combine(DecodeAliasPath(project[2]), "__SignatureAssemblyAliases_" + documentId + ".cs");
                                    newPlan = newPrefix + documentId + "|" + fresh + "|" + alias + "|" + EncodeAliasPath(path);
                                    requiredAliases.Add(newPlan);
                                }
                                alias = newPlan.Split('|')[3];
                                text.Append(EscapeIdentifier(alias));
                                continue;
                            }
                            var hostId = requiredAliases.Single(a => a.StartsWith("@source|", StringComparison.Ordinal)
                                && a.EndsWith("|" + EncodeAliasPath(host.FilePath), StringComparison.Ordinal)).Split('|')[1];
                            var prefix = "@global|" + hostId + "|";
                            var planned = requiredAliases.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal)
                                && a.EndsWith("|" + alias, StringComparison.Ordinal));
                            if (planned is null)
                            {
                                var fresh = FreshAssemblyAlias(model.Compilation, requiredAliases);
                                planned = prefix + fresh + "|" + alias;
                                requiredAliases.Add(planned);
                            }
                            alias = planned.Split('|')[2];
                        }
                        else
                        {
                            var scope = scopes.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                            requiredAliases.Add((scope?.SpanStart ?? -1).ToString(CultureInfo.InvariantCulture) + ":" + alias);
                        }
                    }
                    text.Append(EscapeIdentifier(alias));
                    continue;
                }
            }
            text.Append(part.ToString());
        }
        return text.ToString();
    }

    private static string EncodeAliasPath(string path) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
    private static string DecodeAliasPath(string path) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(path));

    private static string FreshAssemblyAlias(Compilation compilation, HashSet<string> requiredAliases)
    {
        var names = compilation.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantTokens())
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        var fresh = "__signatureAssembly";
        for (var suffix = 0; names.Contains(fresh) || requiredAliases.Any(a =>
            a.Contains(":" + fresh + ":", StringComparison.Ordinal) || a.Contains("|" + fresh + "|", StringComparison.Ordinal)); suffix++)
            fresh = "__signatureAssembly" + suffix.ToString(CultureInfo.InvariantCulture);
        return fresh;
    }

    private static string AssemblyTypeIdentity(ITypeSymbol type) => TypeIdentity(type, null, SymbolIdentity)!;

    private static string? TypeIdentity(ITypeSymbol? type, Dictionary<string, string>? originals, Func<IMethodSymbol, string> getSymbolIdentity)
    {
        if (type is null) return null;
        var identity = type switch
        {
            IArrayTypeSymbol array => "array:" + array.Rank + ":" + TypeIdentity(array.ElementType, originals, getSymbolIdentity),
            IPointerTypeSymbol pointer => "pointer:" + TypeIdentity(pointer.PointedAtType, originals, getSymbolIdentity),
            IFunctionPointerTypeSymbol pointer => "function:" + pointer.Signature.CallingConvention + ":"
                + string.Join(",", pointer.Signature.UnmanagedCallingConventionTypes.Select(t => TypeIdentity(t, originals, getSymbolIdentity))) + ":"
                + pointer.Signature.RefKind + ":" + TypeIdentity(pointer.Signature.ReturnType, originals, getSymbolIdentity) + ":"
                + string.Join(",", pointer.Signature.Parameters.Select(p => p.RefKind + ":" + TypeIdentity(p.Type, originals, getSymbolIdentity))),
            INamedTypeSymbol named => named.ContainingAssembly?.Identity + ":"
                + named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":"
                + (named.ContainingType is null ? "" : TypeIdentity(named.ContainingType, originals, getSymbolIdentity))
                + "<" + string.Join(",", named.TypeArguments.Select(t => TypeIdentity(t, originals, getSymbolIdentity))) + ">",
            ITypeParameterSymbol parameter => parameter.Ordinal + ":" + (parameter.ContainingSymbol is IMethodSymbol owner
                ? originals is null ? owner.ContainingAssembly.Identity + ":" + owner.ContainingType.ToDisplayString() + ":" + owner.Name + ":" + owner.Arity
                    : TargetIdentity(owner, originals, getSymbolIdentity)
                : parameter.ContainingSymbol.ContainingAssembly?.Identity + ":" + parameter.ContainingSymbol.ToDisplayString()),
            _ => type.ContainingAssembly?.Identity.ToString(),
        };
        // Roslyn reports None or NotAnnotated for the same nonnullable value-type conversion.
        // Nullable<T> remains a distinct named type; reference/type-parameter annotations remain meaningful.
        return (type.IsValueType ? NullableAnnotation.None : type.NullableAnnotation) + ":" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + identity;
    }

    private static IMethodSymbol CanonicalDefinition(IMethodSymbol method) =>
        (method.ReducedFrom ?? method).OriginalDefinition;

    private sealed class DocumentMethodIdentities
    {
        private readonly Dictionary<IMethodSymbol, string> _identities = new(ReferenceEqualityComparer.Instance);

        internal string Get(IMethodSymbol method)
        {
            if (_identities.TryGetValue(method, out var identity))
            {
                return identity;
            }

            identity = SymbolIdentity(method);
            _identities.Add(method, identity);
            return identity;
        }
    }

    private static string SymbolIdentity(IMethodSymbol method) =>
        method.ContainingAssembly.Identity + ":" + method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + ":" + string.Join(",", method.Parameters.Select(p => p.RefKind + " " + AssemblyTypeIdentity(p.Type)));

    private static string TargetIdentity(IMethodSymbol method, Dictionary<string, string> originals, Func<IMethodSymbol, string> getSymbolIdentity)
    {
        var canonical = CanonicalDefinition(method);
        foreach (var reference in canonical.DeclaringSyntaxReferences)
        {
            var annotation = reference.GetSyntax().GetAnnotations(DeclarationTag).FirstOrDefault();
            if (annotation?.Data is { } key) return key;
        }
        var canonicalIdentity = getSymbolIdentity(canonical);
        if (originals.TryGetValue(canonicalIdentity, out var original)) return original;
        return ReferenceEquals(method, canonical) ? canonicalIdentity : getSymbolIdentity(method);
    }

    private static BindingEvidence CreateEvidence(SyntaxAnnotation annotation, IMethodSymbol target,
        SyntaxNode node, SemanticModel model, Dictionary<string, string> originals, Func<IMethodSymbol, string> getSymbolIdentity)
    {
        var types = node is ExpressionSyntax expression ? model.GetTypeInfo(expression) : default;
        var targetIdentity = TargetIdentity(target, originals, getSymbolIdentity);
        var typeArguments = string.Join(",", target.TypeArguments.Concat(target.ContainingType.TypeArguments)
            .Select(t => TypeIdentity(t, originals, getSymbolIdentity)));
        var typeIdentity = TypeIdentity(types.Type, originals, getSymbolIdentity);
        var convertedTypeIdentity = ReferenceEquals(types.Type, types.ConvertedType)
            ? typeIdentity : TypeIdentity(types.ConvertedType, originals, getSymbolIdentity);
        var conversionIdentity = node is ExpressionSyntax e ? ConversionIdentity(model.GetConversion(e), originals, getSymbolIdentity) : string.Empty;
        return new BindingEvidence(annotation, targetIdentity, typeArguments,
            typeIdentity, convertedTypeIdentity, conversionIdentity, null, node, []);
    }

    private static string ConversionIdentity(Conversion conversion, Dictionary<string, string> originals, Func<IMethodSymbol, string> getSymbolIdentity) =>
        conversion.ToString() + ":" + (conversion.MethodSymbol is { } method ? TargetIdentity(method, originals, getSymbolIdentity) : string.Empty);

    private static async Task<List<string>> ValidateCandidateAsync(
        Solution original, Solution candidate, HashSet<ProjectId> projects,
        Dictionary<DocumentId, List<BindingEvidence>> evidence, Dictionary<string, string> originals,
        Dictionary<ProjectId, List<DiagnosticEvidence>> diagnosticEvidence, CancellationToken ct)
    {

        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var diagnosticTokens = new Dictionary<DocumentId, Dictionary<SyntaxAnnotation, SyntaxToken>>();
        foreach (var projectId in projects)
        {
            // Edited trees are parsed from exact serialized text; retained originals match
            // their exact tree/span. Changed occurrences require surviving token identities.
            var after = await candidate.GetProject(projectId)!.GetCompilationAsync(ct).ConfigureAwait(false);
            if (after is null) throw Refuse("A dependent project cannot be compiled for candidate verification.");
            var remaining = diagnosticEvidence[projectId].ToList();
            foreach (var diagnostic in after.GetDiagnostics(ct).Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            {
                var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
                var document = diagnostic.Location.IsInSource ? candidate.GetDocument(diagnostic.Location.SourceTree!) : null;
                Dictionary<SyntaxAnnotation, SyntaxToken>? tokens = null;
                if (document is not null)
                {
                    if (!diagnosticTokens.TryGetValue(document.Id, out tokens))
                    {
                        var diagnosticRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                        if (diagnosticRoot is null) throw Refuse("The candidate diagnostic syntax is unavailable.");
                        tokens = diagnosticRoot.GetAnnotatedTokens(DiagnosticTag)
                            .SelectMany(token => token.GetAnnotations(DiagnosticTag).Select(annotation => (annotation, token)))
                            .ToDictionary(pair => pair.annotation, pair => pair.token);
                        diagnosticTokens[document.Id] = tokens;
                    }
                }
                var match = remaining.FindIndex(entry =>
                {
                    if (entry.Severity != diagnostic.Severity || entry.Id != diagnostic.Id || entry.Message != message) return false;
                    if (entry.Document is null)
                        return document is null && entry.ExternalIdentity == ExternalDiagnosticIdentity(diagnostic);
                    if (entry.Document != document?.Id) return false;
                    if (ReferenceEquals(entry.SourceTree, diagnostic.Location.SourceTree))
                        return entry.SourceSpan == diagnostic.Location.SourceSpan;
                    return tokens is not null
                        && tokens.TryGetValue(entry.Start!, out var start) && tokens.TryGetValue(entry.End!, out var end)
                        && start.SpanStart + entry.StartOffset == diagnostic.Location.SourceSpan.Start
                        && end.SpanStart + entry.EndOffset == diagnostic.Location.SourceSpan.End;
                });
                if (match >= 0)
                {
                    remaining.RemoveAt(match);
                    continue;
                }
                if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    warnings.Add("Compiler warning " + diagnostic.Id + " introduced by the signature change.");
                    continue;
                }
                throw Refuse("The requested signature change introduces compiler diagnostic " + diagnostic.Id +
                    ". Retain declaration grammar, parameter dependencies, and valid constructor initialization.");
            }
        }
        var entries = evidence.ToArray();
        if (entries.Length > 0)
        {
            ct.ThrowIfCancellationRequested();
            var outcomes = await ReadBatchAsync(entries, async entry =>
            {
                await ValidateDocumentBindingsAsync(candidate.GetDocument(entry.Key)!, entry.Value, originals, ct).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            ConsumeReadOutcomes(outcomes, _ => { }, ct);
        }
        return warnings.OrderBy(w => w, StringComparer.Ordinal).ToList();
    }

    private static async Task ValidateDocumentBindingsAsync(
        Document document, List<BindingEvidence> entries, Dictionary<string, string> originals, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (root is null || model is null) throw Refuse("The candidate semantic model is unavailable.");
        var annotatedNodes = root.DescendantNodesAndSelf()
            .SelectMany(node => node.GetAnnotations(DeclarationTag).Concat(node.GetAnnotations(BindingTag))
                .Concat(node.GetAnnotations(ArgumentTag)).Select(annotation => (annotation, node)))
            .ToDictionary(pair => pair.annotation, pair => pair.node);
        Func<IMethodSymbol, string> getSymbolIdentity = new DocumentMethodIdentities().Get;
        foreach (var entry in entries)
        {
            var retained = ReferenceEquals(entry.OriginalNode.SyntaxTree, root.SyntaxTree)
                && ReferenceEquals(root.SyntaxTree, model.SyntaxTree);
            var node = retained ? entry.OriginalNode : annotatedNodes.GetValueOrDefault(entry.Annotation);
            // An argument intentionally removed by identity has no surviving annotation.
            if (node is null && (entry.Annotation.Kind == ArgumentTag
                || entry.ContainingArguments.Any(a => !annotatedNodes.ContainsKey(a)))) continue;
            if (node is null) throw Refuse("A retained call lost its verification identity.");
            var target = entry.Annotation.Kind == ArgumentTag
                ? ResolveArgument(model, node, ct)?.Parameter?.ContainingSymbol as IMethodSymbol
                : model.GetSymbolInfo(node, ct).Symbol as IMethodSymbol;
            if (target is null) throw Refuse("A retained call or argument no longer has its original binding.");
            var current = CreateEvidence(entry.Annotation, target, node, model, originals, getSymbolIdentity);
            if (entry.Target != current.Target || entry.TypeArguments != current.TypeArguments
                || entry.Type != current.Type || entry.ConvertedType != current.ConvertedType
                || entry.Conversion != current.Conversion)
                throw Refuse("The requested signature change changes a retained target, type substitution, or contextual conversion.");
            if (entry.Parameter is not null
                && ResolveArgument(model, node, ct)?.Parameter?.Name != entry.Parameter)
                throw Refuse("The requested signature change changes a retained argument's parameter binding.");
        }
    }

    private static PublicInvalidOperationException Refuse(string message) => new(message);

    private static async Task<IReadOnlyList<IMethodSymbol>> CollectRelatedSymbolsAsync(
        IMethodSymbol method, Solution solution, CancellationToken ct)
    {
        var set = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Queue<IMethodSymbol>();
        void Add(IMethodSymbol symbol)
        {
            symbol = symbol.OriginalDefinition;
            if (set.Add(symbol)) pending.Enqueue(symbol);
        }
        Add(method);
        while (pending.TryDequeue(out var symbol))
        {
            ct.ThrowIfCancellationRequested();
            if (symbol.ContainingType.TypeKind == TypeKind.Interface)
            {
                foreach (var implementation in await SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false))
                    if (implementation is IMethodSymbol implementationMethod) Add(implementationMethod);
            }
            foreach (var member in SymbolServiceHelpers.GetImplementedInterfaceMembers(symbol)) Add(member);
            if (symbol.OverriddenMethod is { } parent) Add(parent);
            if ((symbol.IsVirtual || symbol.IsAbstract) && !symbol.IsOverride)
            {
                foreach (var overridden in await SymbolFinder.FindOverridesAsync(symbol, solution, cancellationToken: ct).ConfigureAwait(false))
                    if (overridden is IMethodSymbol overriddenMethod) Add(overriddenMethod);
            }
            if (symbol.PartialDefinitionPart is { } definition) Add(definition);
            if (symbol.PartialImplementationPart is { } partialImplementation) Add(partialImplementation);
        }
        return [.. set];
    }
}
