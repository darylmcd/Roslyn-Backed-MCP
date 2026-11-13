using System.Globalization;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests.Helpers;

/// <summary>
/// Formats a child-process result for an assertion message so a failed fixture
/// <c>dotnet restore</c>/<c>dotnet msbuild</c> run reports its exit code, early-kill
/// reason, duration, command line and both output streams instead of output alone.
/// </summary>
internal static class CommandExecutionFailureMessage
{
    internal static string Describe(this CommandExecutionDto result, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var prefix = string.IsNullOrEmpty(context) ? string.Empty : context + Environment.NewLine;
        return string.Create(CultureInfo.InvariantCulture, $"""
            {prefix}child process: {result.Command} {string.Join(' ', result.Arguments)}
            workingDirectory: {result.WorkingDirectory}
            exitCode: {result.ExitCode}; succeeded: {result.Succeeded}; durationMs: {result.DurationMs}; earlyKillReason: {result.EarlyKillReason ?? "<none>"}
            --- stdout ---
            {Tail(result.StdOut)}
            --- stderr ---
            {Tail(result.StdErr)}
            """);
    }

    /// <summary>Maximum characters of each stream kept in a message (the end holds the failure).</summary>
    internal const int MaxStreamChars = 64 * 1024;

    internal static string Tail(string? text, int maxChars = MaxStreamChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        var dropped = text.Length - maxChars;
        return string.Create(CultureInfo.InvariantCulture, $"... [truncated {dropped} chars]{Environment.NewLine}") + text[dropped..];
    }
}
