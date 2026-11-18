using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Chooses which spellings of an extracted concrete type <c>replaceUsages</c> retypes at the generated interface.
/// A spelling is replaced when it types a declaration: a parameter, field, local, property, indexer, event, return type,
/// <c>foreach</c>/<c>out var</c> variable or tuple element, spelled bare, qualified, <c>global::</c>-qualified or through an
/// alias, alone or as the element of a nullable/array/<c>ref</c> type or an argument of a generic type. Positions that are
/// not usages of the type as a type stay concrete: construction, static member access, <c>typeof</c>, casts, <c>is</c>/<c>as</c>,
/// base lists, attributes, constraints, operator and conversion signatures, declaration patterns (<c>is X x</c>), catch
/// declarations and the alias target itself; the compile gate refuses any rewrite that leaves one of them mismatched.
/// </summary>
internal static class ConcreteUsageRewriter
{
    /// <summary>
    /// The outermost spelling of the concrete type for each reference location that sits in a replaceable position of
    /// <paramref name="root"/>, one entry per distinct spelling.
    /// </summary>
    public static IReadOnlyList<TypeSyntax> FindReplaceableSpellings(SyntaxNode root, IEnumerable<TextSpan> referenceSpans) =>
        referenceSpans
            .Select(span => root.FindNode(span))
            .OfType<IdentifierNameSyntax>()
            .Select(OutermostName)
            .Where(IsReplaceable)
            .Distinct()
            .ToArray();

    // `A.B.Echo` and `global::A.Echo` are one spelling; `Echo.Nested` (the type as a qualifier) is not a type spelling.
    private static TypeSyntax OutermostName(TypeSyntax start)
    {
        var name = start;
        while (true)
        {
            switch (name.Parent)
            {
                case QualifiedNameSyntax qualified when qualified.Right == name:
                    name = qualified;
                    continue;
                case AliasQualifiedNameSyntax aliasQualified when aliasQualified.Name == name:
                    name = aliasQualified;
                    continue;
                default:
                    return name;
            }
        }
    }

    private static bool IsReplaceable(TypeSyntax spelling)
    {
        SyntaxNode child = spelling;
        var wrapped = false;
        while (true)
        {
            var parent = child.Parent;
            switch (parent)
            {
                case NullableTypeSyntax nullable when nullable.ElementType == child:
                case ArrayTypeSyntax array when array.ElementType == child:
                case RefTypeSyntax reference when reference.Type == child:
                    child = parent;
                    wrapped = true;
                    continue;
                case TypeArgumentListSyntax { Parent: GenericNameSyntax generic }:
                    child = OutermostName(generic);
                    wrapped = true;
                    continue;
                case TupleElementSyntax { Parent: TupleTypeSyntax tuple } element when element.Type == child:
                    child = tuple;
                    wrapped = true;
                    continue;
                case ParameterSyntax parameter:
                    return parameter.Type == child;
                case VariableDeclarationSyntax variable:
                    return variable.Type == child;
                case ForEachStatementSyntax forEach:
                    return forEach.Type == child;
                case DeclarationExpressionSyntax declaration:
                    return declaration.Type == child;
                case MethodDeclarationSyntax method:
                    return method.ReturnType == child;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.ReturnType == child;
                case DelegateDeclarationSyntax @delegate:
                    return @delegate.ReturnType == child;
                case PropertyDeclarationSyntax property:
                    return property.Type == child;
                case IndexerDeclarationSyntax indexer:
                    return indexer.Type == child;
                case EventDeclarationSyntax @event:
                    return @event.Type == child;
                // `new List<Echo>()` / `new Echo[n]` must agree with the retyped declaration, but `new Echo()` itself stays.
                case ObjectCreationExpressionSyntax creation:
                    return wrapped && creation.Type == child;
                case ArrayCreationExpressionSyntax creation:
                    return wrapped && creation.Type == child;
                default:
                    return false;
            }
        }
    }
}
