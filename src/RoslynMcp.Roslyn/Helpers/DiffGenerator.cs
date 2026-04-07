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
    /// FLAG-6A: hard cap on per-file diff size in characters. If a generated diff would
    /// exceed this, the remainder is truncated with a marker. Caller-facing protection
    /// against single-letter identifier renames and similar pathological cases that can
    /// produce 70 KB+ diffs for trivial logical changes.
    /// </summary>
    public const int DefaultMaxDiffChars = 16 * 1024;

    /// <summary>
    /// Produces a unified-diff string comparing <paramref name="oldText"/> with <paramref name="newText"/>.
    /// The output follows the standard <c>--- a/…</c> / <c>+++ b/…</c> unified format with 3-line context.
    /// FLAG-6A: the output is capped at <see cref="DefaultMaxDiffChars"/> characters; any further
    /// hunks are replaced by a truncation marker so callers always get a self-contained, bounded diff.
    /// </summary>
    /// <param name="oldText">The original text.</param>
    /// <param name="newText">The modified text.</param>
    /// <param name="filePath">The file path used in the diff header lines.</param>
    /// <param name="maxChars">
    /// Optional override for the per-file character cap. Pass <c>0</c> to disable truncation.
    /// </param>
    /// <returns>A string containing the unified diff, or an empty string if there are no differences.</returns>
    public static string GenerateUnifiedDiff(string oldText, string newText, string filePath, int maxChars = DefaultMaxDiffChars)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{filePath}");
        sb.AppendLine($"+++ b/{filePath}");

        var lines = diff.Lines;
        int i = 0;
        var truncated = false;
        var hunksWritten = 0;
        var hunksSkipped = 0;
        while (i < lines.Count)
        {
            if (lines[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            var (contextStart, contextEnd) = FindHunkBounds(lines, i);

            if (truncated)
            {
                // Once we are over the cap, just count remaining hunks for the marker.
                hunksSkipped++;
                i = contextEnd;
                continue;
            }

            var (oldLine, newLine) = ComputeLineNumbers(lines, contextStart);
            var (oldCount, newCount) = CountHunkLines(lines, contextStart, contextEnd);

            // Build the hunk into a separate buffer so we can decide whether to commit it
            // without exceeding the cap. Hunks are atomic — never half-emitted.
            var hunk = new System.Text.StringBuilder();
            hunk.AppendLine($"@@ -{oldLine},{oldCount} +{newLine},{newCount} @@");
            for (int j = contextStart; j < contextEnd; j++)
            {
                var prefix = lines[j].Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    _ => " "
                };
                hunk.AppendLine($"{prefix}{lines[j].Text}");
            }

            if (maxChars > 0 && sb.Length + hunk.Length > maxChars)
            {
                truncated = true;
                hunksSkipped++;
                i = contextEnd;
                continue;
            }

            sb.Append(hunk);
            hunksWritten++;
            i = contextEnd;
        }

        if (truncated)
        {
            sb.AppendLine($"@@ truncated @@");
            sb.AppendLine($"# FLAG-6A: diff exceeded {maxChars} chars; {hunksWritten} hunk(s) shown, {hunksSkipped} hunk(s) omitted.");
            sb.AppendLine($"# Re-read the full file with the Read tool or rerun with a smaller scope.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds the context-padded start and end indices for a diff hunk beginning at
    /// <paramref name="changeStart"/>, merging adjacent hunks within 6-line proximity.
    /// </summary>
    private static (int ContextStart, int ContextEnd) FindHunkBounds(IList<DiffPiece> lines, int changeStart)
    {
        int contextStart = Math.Max(0, changeStart - 3);
        int chunkEnd = changeStart;

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
        return (contextStart, contextEnd);
    }

    /// <summary>
    /// Computes the 1-based old/new line numbers at <paramref name="contextStart"/> by
    /// counting non-inserted (old) and non-deleted (new) lines before it.
    /// </summary>
    private static (int OldLine, int NewLine) ComputeLineNumbers(IList<DiffPiece> lines, int contextStart)
    {
        int oldLine = 1, newLine = 1;
        for (int j = 0; j < contextStart; j++)
        {
            if (lines[j].Type != ChangeType.Inserted) oldLine++;
            if (lines[j].Type != ChangeType.Deleted) newLine++;
        }
        return (oldLine, newLine);
    }

    /// <summary>
    /// Counts old-side and new-side lines within the hunk range.
    /// </summary>
    private static (int OldCount, int NewCount) CountHunkLines(IList<DiffPiece> lines, int start, int end)
    {
        int oldCount = 0, newCount = 0;
        for (int j = start; j < end; j++)
        {
            if (lines[j].Type != ChangeType.Inserted) oldCount++;
            if (lines[j].Type != ChangeType.Deleted) newCount++;
        }
        return (oldCount, newCount);
    }
}
