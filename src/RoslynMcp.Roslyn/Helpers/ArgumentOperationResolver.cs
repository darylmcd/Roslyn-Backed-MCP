using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Maps a call-site argument expression to the <see cref="IArgumentOperation"/> that binds it.
/// Roslyn returns no operation for expressions the semantic model treats as pure syntax wrappers
/// (parentheses, <c>checked(...)</c>, postfix <c>!</c>), so lookups must unwrap them first.
/// </summary>
internal static class ArgumentOperationResolver
{
    /// <summary>
    /// Strips parentheses, <c>checked(...)</c>/<c>unchecked(...)</c> and the postfix null-forgiving
    /// operator, which have no operation of their own, and returns the innermost expression.
    /// </summary>
    internal static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        var operand = expression;
        while (true)
        {
            var inner = operand switch
            {
                ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
                CheckedExpressionSyntax checkedExpression => checkedExpression.Expression,
                PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                    suppression.Operand,
                _ => null,
            };
            if (inner is null)
                return operand;

            operand = inner;
        }
    }

    /// <summary>
    /// Returns the argument operation that owns <paramref name="argumentExpression"/>, or
    /// <see langword="null"/> when none binds it. A params-expanded element resolves to the single
    /// implicit params argument; test <see cref="IArgumentOperation.ArgumentKind"/> to distinguish it.
    /// </summary>
    internal static IArgumentOperation? Resolve(
        SemanticModel model, ExpressionSyntax argumentExpression, CancellationToken ct)
    {
        var operation = model.GetOperation(Unwrap(argumentExpression), ct);
        while (operation is not null && operation is not IArgumentOperation)
            operation = operation.Parent;
        return operation as IArgumentOperation;
    }
}
