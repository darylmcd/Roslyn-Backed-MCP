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
}

/// <summary>
/// Use-site check for <c>extract_shared_expression_to_helper_preview</c>: a generated helper call is a value, so an
/// occurrence whose enclosing syntax needs storage cannot be replaced by it. Decided from the compiler's own binding of
/// the occurrence and its parents, for the example and for every candidate.
/// </summary>
internal static class SharedExpressionUseSite
{
    public static SharedExpressionUseSiteKind Classify(ExpressionSyntax expression, SemanticModel model, CancellationToken ct) =>
        RequiresStorage(expression, model, ct) ? SharedExpressionUseSiteKind.StorageRequired : SharedExpressionUseSiteKind.Eligible;

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
