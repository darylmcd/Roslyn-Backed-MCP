using System.Text.Json;
using RoslynMcp.Host.Stdio.Catalog;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Host.Stdio.ProtocolCompatibility;

/// <summary>
/// Normalizes the LSP <c>character</c> spelling into the server's canonical one-based
/// <c>column</c> argument before SDK binding.
/// </summary>
internal static class LspSourceLocationArgumentNormalizer
{
    public static IDictionary<string, JsonElement>? Normalize(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || !arguments.TryGetValue("character", out var character))
        {
            return arguments;
        }

        var supportsSourceLocation =
            ToolParameterIndex.GetParameter(toolName, "filePath") is { Type: "string" } &&
            ToolParameterIndex.GetParameter(toolName, "line") is { Type: "int" or "int?" } &&
            ToolParameterIndex.GetParameter(toolName, "column") is { Type: "int" or "int?" };
        if (!supportsSourceLocation)
        {
            return arguments;
        }

        if (character.ValueKind != JsonValueKind.Number ||
            !character.TryGetInt32(out var zeroBasedCharacter) ||
            zeroBasedCharacter < 0 ||
            zeroBasedCharacter == int.MaxValue)
        {
            throw new PublicArgumentException(
                "character must be a zero-based UTF-16 integer that can be converted to a one-based column.",
                "character");
        }

        var oneBasedColumn = zeroBasedCharacter + 1;
        if (arguments.TryGetValue("column", out var column) &&
            (column.ValueKind != JsonValueKind.Number ||
             !column.TryGetInt32(out var suppliedColumn) ||
             suppliedColumn != oneBasedColumn))
        {
            throw new PublicArgumentException(
                $"column and character disagree: column must equal character + 1 ({oneBasedColumn}).",
                "character");
        }

        var normalized = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
        normalized.Remove("character");
        normalized.TryAdd("column", JsonSerializer.SerializeToElement(oneBasedColumn));
        return normalized;
    }
}
