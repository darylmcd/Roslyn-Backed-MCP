using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class ParameterObjectTargetContract
{
    /// <summary>
    /// Returns the refusal for a target whose kind or dispatch contract the single-declaration
    /// rewriter cannot rewrite atomically. Must run before any caller collection or
    /// preview construction so no compile-breaking partial rewrite is ever stored as a
    /// redeemable preview token.
    /// </summary>
    internal static string? GetRefusalReason(IMethodSymbol target)
    {
        if (target.MethodKind == MethodKind.LocalFunction)
            return $"parameter_object_preview does not support local functions ({target.ToDisplayString()}); v1 scope is intra-class methods only.";

        if (target.MethodKind == MethodKind.ExplicitInterfaceImplementation || !target.ExplicitInterfaceImplementations.IsEmpty)
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' explicitly implements an interface member. " +
                "The interface declaration would keep the old signature, breaking the implementation relationship; " +
                "change the interface first, then re-run against a free-standing method.";

        if (target.MethodKind is not (MethodKind.Ordinary or MethodKind.ReducedExtension))
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' has method kind '{target.MethodKind}'. " +
                "Constructors, destructors, accessors, operators, conversions, and delegate/anonymous functions " +
                "cannot have their declaration and call sites rewritten atomically; only ordinary methods " +
                "(including extension methods) are supported.";

        if (target.IsOverride)
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' is an 'override'. " +
                "Rewriting it in isolation would leave the base declaration and sibling overrides at the old arity, " +
                "breaking the override relationship; restructure the whole hierarchy manually instead.";

        if (target.IsVirtual || target.IsAbstract)
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' is a " +
                $"{(target.IsAbstract ? "'abstract'" : "'virtual'")} dispatch root. " +
                "Overrides in derived types would keep the old signature; restructure the whole hierarchy manually instead.";

        if (ImplementsInterfaceMember(target))
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' implements an interface member. " +
                "The interface declaration would keep the old signature, breaking the implementation relationship; " +
                "change the interface first, then re-run against a free-standing method.";

        if (target.IsExtern || target.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() is
                    "System.Runtime.InteropServices.DllImportAttribute"
                    or "System.Runtime.InteropServices.LibraryImportAttribute"))
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' is an extern/PInvoke declaration. " +
                "Its signature is bound to a native entry point and cannot be regrouped.";

        if (target.PartialDefinitionPart is not null || target.PartialImplementationPart is not null)
            return $"parameter_object_preview refuses: '{target.ToDisplayString()}' is a partial method with paired " +
                "definition/implementation declarations, which may bind different parameter names. " +
                "Merge the partial declarations first, then re-run.";

        return null;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol target)
        => SymbolServiceHelpers.GetImplementedInterfaceMembers(target).Any();

    internal static string? GetParameterShapeRefusal(IMethodSymbol method, IParameterSymbol parameter)
    {
        if (parameter.RefKind != RefKind.None)
            return $"parameter_object_preview refuses: parameter '{parameter.Name}' has by-ref kind '{parameter.RefKind}'. " +
                "Positional records cannot carry ref/out/in semantics; remove it from parameterNames.";
        if (parameter.IsParams)
            return $"parameter_object_preview refuses: parameter '{parameter.Name}' is a 'params' array. " +
                "Variadic call shape does not survive grouping; remove it from parameterNames.";
        if (method.IsExtensionMethod && parameter.Ordinal == 0)
            return $"parameter_object_preview refuses: parameter '{parameter.Name}' is the extension-method 'this' receiver. " +
                "Drop it from parameterNames; other parameters of the extension method remain eligible.";

        return null;
    }
}
