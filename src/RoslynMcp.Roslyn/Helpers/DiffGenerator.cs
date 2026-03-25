using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Generates unified diff output for a pair of text strings using DiffPlex.
/// </summary>
public static class DiffGenerator
{
    /// <summary>
    /// Produces a unified-diff string comparing <paramref name="oldText"/> with <paramref name="newText"/>.
    /// The output follows the standard <c>--- a/…</c> / <c>+++ b/…</c> unified format with 3-line context.
    /// </summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="filePath">The file path used in the diff header lines.</param>
    /// <returns>A string containing the unified diff, or an empty string if there are no differences.</returns>
    public static string GenerateUnifiedDiff(string oldText, string newText, string filePath)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        var lines = diff.Lines;
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            int contextStart = Math.Max(0, i - 3);
            int chunkEnd = i;

            while (chunkEnd < lines.Count)
            {
                if (lines[chunkEnd].Type != ChangeType.Unchanged)
                {
                    chunkEnd++;
                    continue;
                }

                int nextChange = chunkEnd;
                while (nextChange < lines.Count && lines[nextChange].Type == ChangeType.Unchanged)
                    nextChange++;

                if (nextChange < lines.Count && nextChange - chunkEnd <= 6)
                {
                    chunkEnd = nextChange + 1;
                    continue;
                }

                break;
            }

            int contextEnd = Math.Min(lines.Count, chunkEnd + 3);

            int oldLine = 1, newLine = 1;
            for (int j = 0; j < contextStart; j++)
            {
                if (lines[j].Type != ChangeType.Inserted) oldLine++;
                if (lines[j].Type != ChangeType.Deleted) newLine++;
            }

            int oldCount = 0, newCount = 0;
            for (int j = contextStart; j < contextEnd; j++)
            {
                if (lines[j].Type != ChangeType.Inserted) oldCount++;
                if (lines[j].Type != ChangeType.Deleted) newCount++;
            }

            sb.AppendLine($"@@ -{oldLine},{oldCount} +{newLine},{newCount} @@");

            for (int j = contextStart; j < contextEnd; j++)
            {
                var prefix = lines[j].Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    _ => " "
                };
                sb.AppendLine($"{prefix}{lines[j].Text}");
            }

            i = contextEnd;
        }

        return sb.ToString();
    }
}
