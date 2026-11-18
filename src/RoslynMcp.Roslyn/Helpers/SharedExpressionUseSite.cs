using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>What the enclosing syntax requires of an expression that a shared helper call would replace.</summary>
internal enum SharedExpressionUseSiteKind
{
    /// <summary>A plain value: a value-returning helper call can take its place.</summary>
    Eligible,

    /// <summary>The enclosing syntax needs a variable (assignment target, ref/out/in argument, mutated struct receiver).</summary>
    StorageRequired,

    /// <summary>
    /// The occurrence is inside a lambda that is converted to an <c>Expression&lt;T&gt;</c> (including implicit query
    /// lambdas over <c>IQueryable</c>): a helper call would change the tree a provider receives.
    /// </summary>
    ExpressionTree,

    /// <summary>The occurrence, or the constant expression it belongs to, sits in a slot that only accepts a compile-time constant.</summary>
    ConstantRequired,
}

/// <summary>
/// Use-site check for <c>extract_shared_expression_to_helper_preview</c>: a generated helper call is a value, so an
/// occurrence whose enclosing syntax needs storage cannot be replaced by it. Decided from the compiler's own binding of
/// the occurrence and its parents, for the example and for every candidate.
/// </summary>
internal static class SharedExpressionUseSite
{
    public static SharedExpressionUseSiteKind Classify(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        if (IsInExpressionTree(expression, model, ct)) return SharedExpressionUseSiteKind.ExpressionTree;
        if (RequiresStorage(expression, model, ct)) return SharedExpressionUseSiteKind.StorageRequired;
        return RequiresConstant(expression, model, ct) ? SharedExpressionUseSiteKind.ConstantRequired : SharedExpressionUseSiteKind.Eligible;
    }

    /// <summary>
    /// A constant occurrence is only replaceable where nothing above it needs the constant. Its enclosing constant
    /// expression (`6 * 7 + 1` around `6 * 7`) decides: ancestors merely losing constness is fine
    /// (`int x = 6 * 7 + 1;`), a constant-only slot is not.
    /// </summary>
    private static bool RequiresConstant(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        if (!model.GetConstantValue(expression, ct).HasValue) return false;

        SyntaxNode top = expression;
        while (top.Parent is ExpressionSyntax parent && model.GetConstantValue(parent, ct).HasValue) top = parent;

        return top.Parent switch
        {
            InterpolationAlignmentClauseSyntax => true,
            CaseSwitchLabelSyntax => true,
            ConstantPatternSyntax => true,
            RelationalPatternSyntax => true,
            GotoStatementSyntax { RawKind: (int)SyntaxKind.GotoCaseStatement } => true,
            AttributeArgumentSyntax => true,
            // Not listed, because they never reach this check: enum member values (an enum declaration is not a
            // type declaration, so the shared extraction never treats an occurrence in it as a candidate) and
            // fixed-size buffer lengths (the semantic model reports no constant for them, so their binding cannot
            // be proven equivalent and the extraction is refused by the binding check).
            EqualsValueClauseSyntax { Parent: ParameterSyntax } => true,
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } =>
                model.GetDeclaredSymbol(declarator, ct) is ILocalSymbol { IsConst: true } or IFieldSymbol { IsConst: true },
            _ => false,
        };
    }

    private static bool IsInExpressionTree(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        var expressionType = model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        if (expressionType is null) return false;

        var child = (SyntaxNode)expression;
        for (var parent = child.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            // A lambda nested in a tree is itself part of the tree, so every enclosing lambda counts.
            if (parent is AnonymousFunctionExpressionSyntax function &&
                IsExpressionTreeType(model.GetTypeInfo(function, ct).ConvertedType, expressionType))
            {
                return true;
            }

            // Query clauses have no lambda syntax: the compiler binds them to Queryable.* methods whose
            // parameters are Expression<>.
            if (IsQueryLambdaPart(parent, child) &&
                GetQueryMethod(parent, model, ct) is { } method &&
                method.Parameters.Any(parameter => IsExpressionTreeType(parameter.Type, expressionType)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExpressionTreeType(ITypeSymbol? type, INamedTypeSymbol expressionType) =>
        type is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expressionType);

    /// <summary>True when <paramref name="child"/> is the part of query clause <paramref name="clause"/> that becomes a lambda body.</summary>
    private static bool IsQueryLambdaPart(SyntaxNode clause, SyntaxNode child) => clause switch
    {
        WhereClauseSyntax where => where.Condition == child,
        LetClauseSyntax let => let.Expression == child,
        OrderingSyntax ordering => ordering.Expression == child,
        SelectClauseSyntax select => select.Expression == child,
        GroupClauseSyntax group => group.GroupExpression == child || group.ByExpression == child,
        JoinClauseSyntax join => join.LeftExpression == child || join.RightExpression == child,
        // The first from clause is the query source; a later one becomes a SelectMany selector lambda.
        FromClauseSyntax { Parent: QueryBodySyntax } from => from.Expression == child,
        _ => false,
    };

    private static IMethodSymbol? GetQueryMethod(SyntaxNode clause, SemanticModel model, CancellationToken ct) => clause switch
    {
        QueryClauseSyntax queryClause => model.GetQueryClauseInfo(queryClause, ct).OperationInfo.Symbol as IMethodSymbol,
        SelectOrGroupClauseSyntax selectOrGroup => model.GetSymbolInfo(selectOrGroup, ct).Symbol as IMethodSymbol,
        OrderingSyntax ordering => model.GetSymbolInfo(ordering, ct).Symbol as IMethodSymbol,
        _ => null,
    };

    private static bool RequiresStorage(ExpressionSyntax expression, SemanticModel model, CancellationToken ct)
    {
        var child = (SyntaxNode)expression;
        while (child.Parent is ParenthesizedExpressionSyntax parenthesized) child = parenthesized;

        switch (child.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == child:
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression
                or (int)SyntaxKind.AddressOfExpression }:
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression }:
            case RefExpressionSyntax:
                return true;
            // An explicit ref/out/in keyword needs a variable; an argument for an in or ref readonly parameter written
            // without the keyword takes a value and stays eligible.
            case ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword or (int)SyntaxKind.InKeyword }:
                return true;
            case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
                return IsDeconstructionTarget(tuple);
            case MemberAccessExpressionSyntax member when member.Expression == child:
                // A struct variable that a mutating member reaches: the helper would hand back a copy. Deliberately
                // refused for every non-readonly struct member, even when it happens not to mutate.
                return IsStructVariable(model.GetOperation(expression, ct)) && RequiresStorageThroughMember(member, model, ct);
            default:
                return false;
        }
    }

    private static bool RequiresStorageThroughMember(MemberAccessExpressionSyntax member, SemanticModel model, CancellationToken ct)
    {
        if (member.Parent is InvocationExpressionSyntax invocation && invocation.Expression == member)
        {
            return model.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol { ContainingType.IsValueType: true } method &&
                !method.IsReadOnly && !method.ContainingType.IsReadOnly;
        }

        // `receiver.Field = value`, `receiver.Property++`, `ref receiver.Field` and the like.
        return RequiresStorage(member, model, ct);
    }

    private static bool IsDeconstructionTarget(TupleExpressionSyntax tuple)
    {
        SyntaxNode node = tuple;
        while (node.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax outer }) node = outer;
        return node.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == node,
            ForEachVariableStatementSyntax forEach => forEach.Variable == node,
            _ => false,
        };
    }

    private static bool IsStructVariable(IOperation? operation) =>
        operation is { Type.IsValueType: true } && operation switch
        {
            IFieldReferenceOperation field => !field.Field.IsReadOnly && !field.Field.IsConst,
            ILocalReferenceOperation local => !local.Local.IsConst,
            IParameterReferenceOperation => true,
            IArrayElementReferenceOperation => true,
            IPropertyReferenceOperation property => property.Property.RefKind == RefKind.Ref,
            IInvocationOperation invocation => invocation.TargetMethod.RefKind == RefKind.Ref,
            _ => false,
        };
}
