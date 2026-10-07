using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Shares lexical C# identifier validation. Type/rename callers reject unescaped contextual
/// keywords; extraction members accept contextual names and emit grammar-safe identifier tokens.
/// Reserved keywords require verbatim spelling in both policies. Unicode identifiers remain valid.
/// </summary>
internal static class IdentifierValidation
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with a descriptive message when
    /// <paramref name="newName"/> is not a legal C# identifier for use as a rename target.
    /// </summary>
    /// <param name="newName">The proposed identifier. May include a leading <c>@</c> for verbatim form.</param>
    /// <param name="parameterLabel">Human-readable label for the parameter (e.g. "new name").</param>
    public static void ThrowIfInvalidIdentifier(string newName, string parameterLabel = "new name")
        => ThrowIfInvalidIdentifierCore(newName, parameterLabel, allowContextualKeywords: false);

    /// <summary>Publishes fixed caller guidance while retaining the detailed validator failure as an inner exception.</summary>
    public static void ThrowIfInvalidPublicIdentifier(
        string name, string parameterName, bool allowContextualKeywords)
    {
        try
        {
            ThrowIfInvalidIdentifierCore(name, parameterName, allowContextualKeywords);
        }
        catch (InvalidOperationException exception)
        {
            throw new PublicArgumentException(
                $"Provide a valid C# identifier for {parameterName}. Use a verbatim identifier for a keyword.",
                parameterName, exception);
        }
    }

    /// <summary>Escapes contextual names so invocation parsing cannot reinterpret them as language constructs.</summary>
    /// <remarks>The caller must first validate the non-null, nonempty name with the extraction-member policy.</remarks>
    public static SyntaxToken CreateMemberIdentifierToken(string name)
    {
        var spelling = name[0] != '@' && SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;
        return SyntaxFactory.ParseToken(spelling);
    }

    private static void ThrowIfInvalidIdentifierCore(
        string newName, string parameterLabel, bool allowContextualKeywords)
    {
        if (string.IsNullOrEmpty(newName))
            throw new InvalidOperationException($"The {parameterLabel} is required.");

        // Verbatim form: strip the leading '@' before validating, then skip the keyword
        // guards because the verbatim form is exactly the way to use a keyword as an
        // identifier. SyntaxFacts.IsValidIdentifier does NOT accept the leading '@'
        // (returns false), so we must do this ourselves.
        var isVerbatim = newName[0] == '@';
        var coreName = isVerbatim ? newName[1..] : newName;

        if (string.IsNullOrEmpty(coreName))
            throw new InvalidOperationException($"'{newName}' is not a valid C# identifier.");

        if (!SyntaxFacts.IsValidIdentifier(coreName))
            throw new InvalidOperationException($"'{newName}' is not a valid C# identifier.");

        if (isVerbatim)
            return; // verbatim form bypasses keyword guards by design

        // SyntaxFacts.IsValidIdentifier accepts reserved keywords like "class".
        // Reserved keywords always require verbatim spelling. Contextual keywords require it
        // only under the strict type/rename policy; those callers must
        // pass the verbatim form (e.g. "@class").
        if (SyntaxFacts.GetKeywordKind(coreName) != SyntaxKind.None)
            throw new InvalidOperationException(
                $"'{newName}' is a reserved C# keyword. Prefix with '@' (e.g. '@{newName}') to use it verbatim.");

        if (!allowContextualKeywords && SyntaxFacts.GetContextualKeywordKind(coreName) != SyntaxKind.None)
            throw new InvalidOperationException(
                $"'{newName}' is a contextual C# keyword and cannot be used as an identifier without '@'.");
    }
}
