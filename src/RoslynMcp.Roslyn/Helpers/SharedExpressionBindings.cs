using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Compares selected expressions in their original project compilation.</summary>
internal sealed class SharedExpressionBindings
{
    private readonly ExpressionSyntax _expression;
    private readonly IOperation? _operation;
    private readonly TypeInfo _typeInfo;
    private readonly Conversion _conversion;
    private readonly Dictionary<ISymbol, int> _captureSlots;
    private Dictionary<(int Kind, TextSpan Span), int>? _syntaxSlots;

    public SharedExpressionBindings(
        ExpressionSyntax expression, BindingContext context, IEnumerable<ISymbol> captures, CancellationToken ct)
    {
        _expression = expression;
        Context = context;
        _operation = GetBoundOperation(expression, context, ct);
        _typeInfo = context.Model.GetTypeInfo(expression, ct);
        _conversion = context.Model.GetConversion(expression, ct);
        _captureSlots = captures.Select((symbol, slot) => (symbol, slot))
            .ToDictionary(pair => pair.symbol, pair => pair.slot, SymbolEqualityComparer.Default);
    }

    public BindingContext Context { get; }

    public bool Matches(SharedExpressionBindings candidate, CancellationToken ct)
    {
        if (!ReferenceEquals(Context.Model.Compilation, candidate.Context.Model.Compilation) ||
            _operation is null || candidate._operation is null ||
            !SameType(_typeInfo.Type, candidate._typeInfo.Type) ||
            !SameType(_typeInfo.ConvertedType, candidate._typeInfo.ConvertedType) ||
            _conversion.IsDynamic || candidate._conversion.IsDynamic ||
            _conversion != candidate._conversion ||
            !SameSymbol(_conversion.MethodSymbol, candidate._conversion.MethodSymbol) ||
            !SameType(_conversion.ConstrainedToType, candidate._conversion.ConstrainedToType))
        {
            return false;
        }

        var pending = new Stack<(IOperation Example, IOperation Candidate)>();
        pending.Push((_operation, candidate._operation));
        while (pending.TryPop(out var pair))
        {
            ct.ThrowIfCancellationRequested();
            var (left, right) = pair;
            if (left.Kind != right.Kind || left.IsImplicit != right.IsImplicit ||
                !SameType(left.Type, right.Type) || !SameOptionalConstant(left.ConstantValue, right.ConstantValue) ||
                left.ChildOperations.Count != right.ChildOperations.Count ||
                !MatchesPayload(left, right, candidate, ct))
            {
                return false;
            }

            // Roslyn exposes type expressions and method groups under nameof as opaque
            // operations. Their exact operand binding was compared through the semantic model.
            if (left is INameOfOperation { Argument.Kind: OperationKind.None })
                continue;

            var rightChildren = right.ChildOperations.GetEnumerator();
            foreach (var leftChild in left.ChildOperations)
            {
                rightChildren.MoveNext();
                pending.Push((leftChild, rightChildren.Current));
            }
        }

        return true;
    }

    private static IOperation? GetBoundOperation(ExpressionSyntax expression, BindingContext context, CancellationToken ct)
    {
        if (context.HasErrors(expression.Span, ct))
            return null;

        // These wrappers do not always have separate C# operations.
        var operand = ArgumentOperationResolver.Unwrap(expression);
        var operation = context.Model.GetOperation(operand, ct);
        // A contextual conversion can wrap the operation returned for the selected syntax.
        while (operation?.Parent is (IConversionOperation or IDelegateCreationOperation or
            IInterpolatedStringHandlerCreationOperation) and { IsImplicit: true } parent &&
            (parent.Syntax.Span == expression.Span || parent.Syntax.Span == operand.Span))
        {
            operation = parent;
        }

        return operation;
    }

    private bool MatchesPayload(IOperation example, IOperation candidate, SharedExpressionBindings other, CancellationToken ct)
    {
        if (example is IPatternOperation pattern && candidate is IPatternOperation otherPattern &&
            (!SameType(pattern.InputType, otherPattern.InputType) || !SameType(pattern.NarrowedType, otherPattern.NarrowedType)))
        {
            return false;
        }

        // Compare the extra binding data for supported operation kinds. Other kinds may
        // bind targets or conversions that are absent from the generic child list.
        return (example, candidate) switch
        {
            (IInvocationOperation a, IInvocationOperation b) =>
                SameSymbol(a.TargetMethod, b.TargetMethod) && a.IsVirtual == b.IsVirtual &&
                SameType(a.ConstrainedToType, b.ConstrainedToType) &&
                SameSymbol(GetInterceptor(a, ct), other.GetInterceptor(b, ct)) &&
                MatchesConditionalEmission(a.TargetMethod, other, ct),
            (IArgumentOperation a, IArgumentOperation b) =>
                a.ArgumentKind == b.ArgumentKind && SameSymbol(a.Parameter, b.Parameter) &&
                SameConversion(a.InConversion, b.InConversion) && SameConversion(a.OutConversion, b.OutConversion),
            (IObjectCreationOperation a, IObjectCreationOperation b) => SameSymbol(a.Constructor, b.Constructor),
            (ICollectionExpressionOperation a, ICollectionExpressionOperation b) =>
                SameSymbol(a.ConstructMethod, b.ConstructMethod) && HasVisibleCollectionBindings(a),
            (IImplicitIndexerReferenceOperation a, IImplicitIndexerReferenceOperation b) =>
                SameSymbol(a.IndexerSymbol, b.IndexerSymbol) && SameSymbol(a.LengthSymbol, b.LengthSymbol),
            (IFieldReferenceOperation a, IFieldReferenceOperation b) =>
                SameMember(a, b) && a.IsDeclaration == b.IsDeclaration,
            (IMethodReferenceOperation a, IMethodReferenceOperation b) =>
                SameMember(a, b) && a.IsVirtual == b.IsVirtual,
            (IPropertyReferenceOperation a, IPropertyReferenceOperation b) => SameMember(a, b),
            (IEventReferenceOperation a, IEventReferenceOperation b) => SameMember(a, b),
            (IEventAssignmentOperation a, IEventAssignmentOperation b) => a.Adds == b.Adds,
            (IInstanceReferenceOperation a, IInstanceReferenceOperation b) => a.ReferenceKind == b.ReferenceKind,
            (IConversionOperation a, IConversionOperation b) =>
                a.IsChecked == b.IsChecked && a.IsTryCast == b.IsTryCast && SameConversion(a.Conversion, b.Conversion),
            (IBinaryOperation a, IBinaryOperation b) =>
                a.OperatorKind == b.OperatorKind && a.IsChecked == b.IsChecked && a.IsLifted == b.IsLifted &&
                a.IsCompareText == b.IsCompareText && SameSymbol(a.OperatorMethod, b.OperatorMethod) &&
                SameType(a.ConstrainedToType, b.ConstrainedToType),
            (IUnaryOperation a, IUnaryOperation b) =>
                a.OperatorKind == b.OperatorKind && a.IsChecked == b.IsChecked && a.IsLifted == b.IsLifted &&
                SameSymbol(a.OperatorMethod, b.OperatorMethod) && SameType(a.ConstrainedToType, b.ConstrainedToType),
            (ICompoundAssignmentOperation a, ICompoundAssignmentOperation b) =>
                a.OperatorKind == b.OperatorKind && a.IsChecked == b.IsChecked && a.IsLifted == b.IsLifted &&
                SameSymbol(a.OperatorMethod, b.OperatorMethod) && SameType(a.ConstrainedToType, b.ConstrainedToType) &&
                SameConversion(a.InConversion, b.InConversion) && SameConversion(a.OutConversion, b.OutConversion),
            (IIncrementOrDecrementOperation a, IIncrementOrDecrementOperation b) =>
                a.IsPostfix == b.IsPostfix && a.IsChecked == b.IsChecked && a.IsLifted == b.IsLifted &&
                SameSymbol(a.OperatorMethod, b.OperatorMethod) && SameType(a.ConstrainedToType, b.ConstrainedToType),
            (ISimpleAssignmentOperation a, ISimpleAssignmentOperation b) => a.IsRef == b.IsRef,
            (IConditionalOperation a, IConditionalOperation b) => a.IsRef == b.IsRef,
            (ICoalesceOperation a, ICoalesceOperation b) => SameConversion(a.ValueConversion, b.ValueConversion),
            (IRangeOperation a, IRangeOperation b) => a.IsLifted == b.IsLifted && SameSymbol(a.Method, b.Method),
            (IWithOperation a, IWithOperation b) => SameSymbol(a.CloneMethod, b.CloneMethod),
            (ITupleOperation a, ITupleOperation b) => SameType(a.NaturalType, b.NaturalType),
            (ITypeOfOperation a, ITypeOfOperation b) => SameType(a.TypeOperand, b.TypeOperand),
            (ISizeOfOperation a, ISizeOfOperation b) => SameType(a.TypeOperand, b.TypeOperand),
            (INameOfOperation a, INameOfOperation b) => MatchesNameOf(a.Argument, b.Argument, other, ct),
            (IIsTypeOperation a, IIsTypeOperation b) =>
                a.IsNegated == b.IsNegated && SameType(a.TypeOperand, b.TypeOperand),
            (IDeclarationPatternOperation a, IDeclarationPatternOperation b) =>
                a.MatchesNull == b.MatchesNull && SameType(a.MatchedType, b.MatchedType) &&
                MatchesValueSymbol(a.DeclaredSymbol, b.DeclaredSymbol, other, ct),
            (ITypePatternOperation a, ITypePatternOperation b) => SameType(a.MatchedType, b.MatchedType),
            (IRecursivePatternOperation a, IRecursivePatternOperation b) =>
                SameType(a.MatchedType, b.MatchedType) && SameSymbol(a.DeconstructSymbol, b.DeconstructSymbol) &&
                MatchesValueSymbol(a.DeclaredSymbol, b.DeclaredSymbol, other, ct),
            (IListPatternOperation a, IListPatternOperation b) =>
                SameSymbol(a.IndexerSymbol, b.IndexerSymbol) && SameSymbol(a.LengthSymbol, b.LengthSymbol) &&
                MatchesValueSymbol(a.DeclaredSymbol, b.DeclaredSymbol, other, ct),
            (ISlicePatternOperation a, ISlicePatternOperation b) => SameSymbol(a.SliceSymbol, b.SliceSymbol),
            (IBinaryPatternOperation a, IBinaryPatternOperation b) => a.OperatorKind == b.OperatorKind,
            (IRelationalPatternOperation a, IRelationalPatternOperation b) => a.OperatorKind == b.OperatorKind,
            (ISwitchExpressionOperation a, ISwitchExpressionOperation b) => a.IsExhaustive == b.IsExhaustive,
            (ISwitchExpressionArmOperation a, ISwitchExpressionArmOperation b) =>
                MatchesValues(a.Locals, b.Locals, other, ct),
            (IAnonymousFunctionOperation a, IAnonymousFunctionOperation b) => MatchesLambda(a.Symbol, b.Symbol, other, ct),
            (IBlockOperation a, IBlockOperation b) => MatchesValues(a.Locals, b.Locals, other, ct),
            (IVariableDeclaratorOperation a, IVariableDeclaratorOperation b) =>
                MatchesValueSymbol(a.Symbol, b.Symbol, other, ct),
            (ILocalReferenceOperation a, ILocalReferenceOperation b) =>
                a.IsDeclaration == b.IsDeclaration && MatchesValueSymbol(a.Local, b.Local, other, ct),
            (IParameterReferenceOperation a, IParameterReferenceOperation b) =>
                MatchesValueSymbol(a.Parameter, b.Parameter, other, ct),
            (IUtf8StringOperation a, IUtf8StringOperation b) => a.Value == b.Value,
            (IConditionalAccessInstanceOperation a, IConditionalAccessInstanceOperation b) =>
                HasSelectedConditionalReceiver(a) && other.HasSelectedConditionalReceiver(b),
            // For these kinds the base operation data and children carry the binding.
            (IArrayCreationOperation or IArrayInitializerOperation or IArrayElementReferenceOperation or
                IAnonymousObjectCreationOperation or ITypeParameterObjectCreationOperation or
                IObjectOrCollectionInitializerOperation or IMemberInitializerOperation or
                IConditionalAccessOperation or IParenthesizedOperation or
                ICoalesceAssignmentOperation or IDefaultValueOperation or ILiteralOperation or
                IInterpolatedStringOperation or IInterpolationOperation or IInterpolatedStringTextOperation or
                IIsPatternOperation or IConstantPatternOperation or IPropertySubpatternOperation or
                INegatedPatternOperation or IDiscardPatternOperation or IDiscardOperation or
                IDelegateCreationOperation or IDeclarationExpressionOperation or IVariableDeclarationGroupOperation or
                IVariableDeclarationOperation or IVariableInitializerOperation or IExpressionStatementOperation or
                IReturnOperation or IThrowOperation or IEmptyOperation, _) => true,
            _ => false,
        };
    }

    private static bool HasVisibleCollectionBindings(ICollectionExpressionOperation operation) =>
        // Elements do not expose the Add selected by a constructible collection initializer.
        operation.Elements.IsEmpty ||
        operation.Type?.TypeKind is TypeKind.Array or TypeKind.Interface ||
        (operation.Type?.TypeKind != TypeKind.TypeParameter &&
            operation.ConstructMethod is not { MethodKind: MethodKind.Constructor });

    private IMethodSymbol? GetInterceptor(IInvocationOperation invocation, CancellationToken ct) =>
        !invocation.IsImplicit && invocation.Syntax is InvocationExpressionSyntax syntax
            ? Context.Model.GetInterceptorMethod(syntax, ct)
            : null;

    private bool MatchesConditionalEmission(IMethodSymbol target, SharedExpressionBindings other, CancellationToken ct)
    {
        var method = (target.ReducedFrom ?? target).OriginalDefinition;
        // The compiler can omit a conditional call during lowering. Equal file
        // definitions prove the same emission without decoding the attributes again.
        return !method.IsConditional || _expression.SyntaxTree == other._expression.SyntaxTree ||
            (Context.GetDefinedSymbols(ct) is { } exampleDefines &&
                other.Context.GetDefinedSymbols(ct) is { } candidateDefines &&
                exampleDefines.SetEquals(candidateDefines));
    }

    private bool MatchesNameOf(IOperation a, IOperation b, SharedExpressionBindings other, CancellationToken ct)
    {
        if (a.Kind != OperationKind.None || b.Kind != OperationKind.None)
            return a.Kind == b.Kind;

        if (a.Syntax is not ExpressionSyntax left || b.Syntax is not ExpressionSyntax right)
            return false;

        var leftSymbol = Context.Model.GetSymbolInfo(left, ct).Symbol;
        var rightSymbol = other.Context.Model.GetSymbolInfo(right, ct).Symbol;
        if (leftSymbol is not null || rightSymbol is not null)
            return MatchesValueSymbol(leftSymbol, rightSymbol, other, ct);

        var leftGroup = Context.Model.GetMemberGroup(left, ct);
        var rightGroup = other.Context.Model.GetMemberGroup(right, ct);
        return !leftGroup.IsEmpty && leftGroup.Length == rightGroup.Length &&
            leftGroup.ToHashSet(SymbolEqualityComparer.Default).SetEquals(rightGroup);
    }

    private bool HasSelectedConditionalReceiver(IConditionalAccessInstanceOperation instance)
    {
        for (var ancestor = instance.Parent;
            ancestor is not null && _expression.Span.Contains(ancestor.Syntax.Span);
            ancestor = ancestor.Parent)
        {
            if (ancestor is IConditionalAccessOperation)
                return true;
        }

        return false;
    }

    private bool MatchesLambda(IMethodSymbol a, IMethodSymbol b, SharedExpressionBindings other, CancellationToken ct) =>
        a.IsAsync == b.IsAsync && a.RefKind == b.RefKind && SameType(a.ReturnType, b.ReturnType) &&
        a.GetAttributes().IsEmpty && b.GetAttributes().IsEmpty &&
        a.GetReturnTypeAttributes().IsEmpty && b.GetReturnTypeAttributes().IsEmpty &&
        MatchesValues(a.Parameters, b.Parameters, other, ct);

    private bool MatchesValues<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, SharedExpressionBindings other, CancellationToken ct) where T : ISymbol
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!MatchesValueSymbol(a[i], b[i], other, ct))
                return false;
        }

        return true;
    }

    private bool MatchesValueSymbol(ISymbol? a, ISymbol? b, SharedExpressionBindings other, CancellationToken ct)
    {
        if (a is null || b is null)
            return a is null && b is null;

        // The current capture list can also contain symbols declared within the selection.
        // Give those declarations their own syntax identity, independent of capture slots.
        var aDeclaration = GetDeclarationSlot(a, ct);
        var bDeclaration = other.GetDeclarationSlot(b, ct);
        if (aDeclaration is not null || bDeclaration is not null)
        {
            if (aDeclaration is not >= 0 || aDeclaration != bDeclaration)
                return false;

            return (a, b) switch
            {
                (ILocalSymbol x, ILocalSymbol y) => SameType(x.Type, y.Type) && x.RefKind == y.RefKind &&
                    x.ScopedKind == y.ScopedKind && x.IsConst == y.IsConst &&
                    x.HasConstantValue == y.HasConstantValue &&
                    (!x.HasConstantValue || SameConstant(x.ConstantValue, y.ConstantValue)),
                (IParameterSymbol x, IParameterSymbol y) => SameType(x.Type, y.Type) && x.RefKind == y.RefKind &&
                    x.ScopedKind == y.ScopedKind && x.Ordinal == y.Ordinal && x.IsParams == y.IsParams &&
                    x.IsDiscard == y.IsDiscard && x.IsOptional == y.IsOptional &&
                    x.HasExplicitDefaultValue == y.HasExplicitDefaultValue &&
                    (!x.HasExplicitDefaultValue || SameConstant(x.ExplicitDefaultValue, y.ExplicitDefaultValue)) &&
                    x.GetAttributes().IsEmpty && y.GetAttributes().IsEmpty,
                _ => false,
            };
        }

        var aCaptured = _captureSlots.TryGetValue(a, out var aSlot);
        var bCaptured = other._captureSlots.TryGetValue(b, out var bSlot);
        return aCaptured || bCaptured
            ? aCaptured && bCaptured && aSlot == bSlot && GetRefKind(a) == GetRefKind(b)
            : SameSymbol(a, b);
    }

    private int? GetDeclarationSlot(ISymbol symbol, CancellationToken ct)
    {
        if (symbol.DeclaringSyntaxReferences is not [var reference] ||
            reference.SyntaxTree != _expression.SyntaxTree || !_expression.Span.Contains(reference.Span))
        {
            return null;
        }

        // Structural equivalence aligns the syntax walk even with different whitespace.
        _syntaxSlots ??= _expression.DescendantNodesAndSelf().Select((node, slot) => (node, slot))
            .ToDictionary(pair => (pair.node.RawKind, pair.node.Span), pair => pair.slot);
        var declaration = reference.GetSyntax(ct);
        return _syntaxSlots.GetValueOrDefault((declaration.RawKind, declaration.Span), -1);
    }

    private static RefKind GetRefKind(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.RefKind,
        IParameterSymbol parameter => parameter.RefKind,
        _ => RefKind.None,
    };

    private static bool SameMember(IMemberReferenceOperation a, IMemberReferenceOperation b) =>
        SameSymbol(a.Member, b.Member) && SameType(a.ConstrainedToType, b.ConstrainedToType);

    private static bool SameConversion(CommonConversion a, CommonConversion b) =>
        a.Exists == b.Exists && a.IsIdentity == b.IsIdentity && a.IsImplicit == b.IsImplicit &&
        a.IsNullable == b.IsNullable && a.IsNumeric == b.IsNumeric && a.IsReference == b.IsReference &&
        a.IsUserDefined == b.IsUserDefined &&
        SameSymbol(a.MethodSymbol, b.MethodSymbol) && SameType(a.ConstrainedToType, b.ConstrainedToType);

    private static bool SameType(ITypeSymbol? a, ITypeSymbol? b) =>
        a?.TypeKind is not (TypeKind.Error or TypeKind.Dynamic) &&
        b?.TypeKind is not (TypeKind.Error or TypeKind.Dynamic) && SameSymbol(a, b);

    private static bool SameSymbol(ISymbol? a, ISymbol? b) => SymbolEqualityComparer.Default.Equals(a, b);

    private static bool SameOptionalConstant(Optional<object?> a, Optional<object?> b) =>
        a.HasValue == b.HasValue && (!a.HasValue || SameConstant(a.Value, b.Value));

    private static bool SameConstant(object? a, object? b) => (a, b) switch
    {
        (double x, double y) => BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y),
        (float x, float y) => BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y),
        (decimal x, decimal y) => decimal.GetBits(x).AsSpan().SequenceEqual(decimal.GetBits(y)),
        _ => Equals(a, b),
    };

    internal sealed class BindingContext(SemanticModel model)
    {
        private ImmutableArray<TextSpan> _errors;
        private HashSet<string>? _definedSymbols;
        private bool _hasSyntaxErrors;

        public SemanticModel Model { get; } = model;

        public bool HasErrors(TextSpan span, CancellationToken ct)
        {
            if (_errors.IsDefault)
            {
                _errors = Model.GetDiagnostics(cancellationToken: ct)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.IsInSource)
                    .Select(diagnostic => diagnostic.Location.SourceSpan)
                    .ToImmutableArray();
            }

            return _errors.Any(error => span.IntersectsWith(error));
        }

        public HashSet<string>? GetDefinedSymbols(CancellationToken ct)
        {
            if (_definedSymbols is not null || _hasSyntaxErrors)
                return _definedSymbols;

            var tree = Model.SyntaxTree;
            var root = tree.GetRoot(ct);
            _hasSyntaxErrors = root.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (_hasSyntaxErrors)
                return null;

            var symbols = new HashSet<string>(tree.Options.PreprocessorSymbolNames, StringComparer.Ordinal);
            foreach (var trivia in root.DescendantTrivia())
            {
                switch (trivia.GetStructure())
                {
                    case DefineDirectiveTriviaSyntax { IsActive: true } define:
                        symbols.Add(define.Name.ValueText);
                        break;
                    case UndefDirectiveTriviaSyntax { IsActive: true } undefine:
                        symbols.Remove(undefine.Name.ValueText);
                        break;
                }
            }

            _definedSymbols = symbols;
            return symbols;
        }
    }
}
