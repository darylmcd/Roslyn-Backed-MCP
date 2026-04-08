using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Validates that a string is a legal C# identifier suitable for rename refactorings.
/// Rejects empty strings, names that are not valid C# identifiers (numeric prefixes,
/// invalid characters), reserved keywords, and contextual keywords.
///
/// Verbatim identifiers (e.g. <c>@class</c>) are accepted: the leading <c>@</c> is
/// stripped before the validity check, and the keyword guards are skipped because the
/// whole point of the verbatim form is to permit a reserved word as an identifier.
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
        // Reject reserved and contextual keywords; callers that want to use them must
        // pass the verbatim form (e.g. "@class").
        if (SyntaxFacts.GetKeywordKind(coreName) != SyntaxKind.None)
            throw new InvalidOperationException(
                $"'{newName}' is a reserved C# keyword. Prefix with '@' (e.g. '@{newName}') to use it verbatim.");

        if (SyntaxFacts.GetContextualKeywordKind(coreName) != SyntaxKind.None)
            throw new InvalidOperationException(
                $"'{newName}' is a contextual C# keyword and cannot be used as an identifier without '@'.");
    }
}
