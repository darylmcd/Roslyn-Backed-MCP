using Microsoft.CodeAnalysis;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Decides whether a type already implements the contract an interface extraction would generate. The generated interface is
/// non-generic, so only non-generic interfaces of the requested name can be that contract (<c>IFoo&lt;T&gt;</c> is a different type).
/// </summary>
internal static class ImplementedInterfaceContract
{
    /// <summary>The non-generic interfaces named <paramref name="name"/> that <paramref name="type"/> implements, directly or inherited.</summary>
    public static IReadOnlyList<INamedTypeSymbol> FindNamed(INamedTypeSymbol type, string name) =>
        type.AllInterfaces
            .Where(candidate => candidate.Arity == 0 && string.Equals(candidate.Name, name, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// The first of <paramref name="named"/> whose members (its own and its base interfaces') are implemented by every member in
    /// <paramref name="requested"/>, or null. A member that implements the interface only explicitly does not count, and an empty
    /// request is never covered (the extraction's own "no members" refusal must still fire).
    /// </summary>
    public static INamedTypeSymbol? FindCovering(
        INamedTypeSymbol type, IReadOnlyList<INamedTypeSymbol> named, IReadOnlyCollection<ISymbol> requested)
    {
        if (requested.Count == 0) return null;
        return named.FirstOrDefault(candidate => Covers(type, candidate, requested));
    }

    private static bool Covers(INamedTypeSymbol type, INamedTypeSymbol contract, IReadOnlyCollection<ISymbol> requested)
    {
        var implemented = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in contract.AllInterfaces.Prepend(contract).SelectMany(candidate => candidate.GetMembers()))
        {
            if (type.FindImplementationForInterfaceMember(member) is { } implementation)
                implemented.Add(implementation.OriginalDefinition);
        }

        return requested.All(member => implemented.Contains(member.OriginalDefinition));
    }
}
