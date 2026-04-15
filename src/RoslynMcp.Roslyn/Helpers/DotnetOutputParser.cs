using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Parses <c>dotnet build</c> and <c>dotnet test</c> output into structured DTOs.
/// </summary>
internal static partial class DotnetOutputParser
{
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)(,(?<endLine>\d+),(?<endColumn>\d+))?\): (?<severity>error|warning) (?<id>[A-Z]{2,}\d+): (?<message>.+?)( \[(?<project>.+)\])?$", RegexOptions.Compiled)]
    private static partial Regex DiagnosticRegex();

    [GeneratedRegex(@"MSB(3027|3021)", RegexOptions.Compiled)]
    private static partial Regex MsBuildFileLockRegex();

    private const int FailureTailMaxChars = 2000;

    /// <summary>
    /// Parses MSBuild-style diagnostic lines from <c>dotnet build</c> standard output.
    /// </summary>
    /// <param name="output">The raw standard output captured from the build process.</param>
    /// <returns>A list of parsed diagnostics. Lines that do not match the expected format are silently skipped.</returns>
    public static IReadOnlyList<DiagnosticDto> ParseBuildDiagnostics(string output)
    {
        var seen = new HashSet<(string Id, string File, int? Line, int? Col)>();
        var diagnostics = new List<DiagnosticDto>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DiagnosticRegex().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var id = match.Groups["id"].Value;
            var file = match.Groups["file"].Value;
            var startLine = ParseNullableInt(match.Groups["line"].Value);
            var startColumn = ParseNullableInt(match.Groups["column"].Value);

            // Deduplicate by (Id, FilePath, Line, Column) to avoid MSBuild retry duplicates
            if (!seen.Add((id, file, startLine, startColumn)))
                continue;

            diagnostics.Add(new DiagnosticDto(
                Id: id,
                Message: match.Groups["message"].Value,
                Severity: Capitalize(match.Groups["severity"].Value),
                Category: "Build",
                FilePath: file,
                StartLine: startLine,
                StartColumn: startColumn,
                EndLine: ParseNullableInt(match.Groups["endLine"].Value),
                EndColumn: ParseNullableInt(match.Groups["endColumn"].Value)));
        }

        return diagnostics;
    }

    /// <summary>
    /// Parses one or more TRX files produced by <c>dotnet test</c> (solution runs may emit multiple TRX files)
    /// into a single aggregated <see cref="TestRunResultDto"/>. When no TRX files exist and the
    /// underlying command failed, the result carries a populated <see cref="TestRunFailureEnvelopeDto"/>
    /// so callers see a structured classification (file lock / build failure / unknown) instead of
    /// a bare invocation error.
    /// </summary>
    public static TestRunResultDto ParseTestRun(
        CommandExecutionDto execution,
        IReadOnlyList<string> trxPaths)
    {
        if (trxPaths.Count == 0)
        {
            var envelope = execution.Succeeded
                ? null
                : ClassifyNoTrxFailure(execution);

            return new TestRunResultDto(
                execution,
                Total: 0,
                Passed: 0,
                Failed: execution.Succeeded ? 0 : 1,
                Skipped: 0,
                Failures: [],
                FailureEnvelope: envelope);
        }

        var total = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var failures = new List<TestFailureDto>();

        foreach (var trxPath in trxPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var one = ParseSingleTrxFile(trxPath);
            total += one.Total;
            passed += one.Passed;
            failed += one.Failed;
            skipped += one.Skipped;
            failures.AddRange(one.Failures);
        }

        return new TestRunResultDto(execution, total, passed, failed, skipped, failures);
    }

    /// <summary>
    /// Builds a synthetic <see cref="TestRunResultDto"/> for the case where the test invocation
    /// was cancelled by a timeout before <c>dotnet test</c> could produce any output. Callers
    /// supply the truncated <see cref="CommandExecutionDto"/> shell and the timeout message.
    /// </summary>
    public static TestRunResultDto BuildTimeoutResult(CommandExecutionDto execution, string timeoutSummary)
    {
        var envelope = new TestRunFailureEnvelopeDto(
            ErrorKind: "Timeout",
            IsRetryable: false,
            Summary: timeoutSummary,
            StdOutTail: Tail(execution.StdOut),
            StdErrTail: Tail(execution.StdErr));

        return new TestRunResultDto(
            execution,
            Total: 0,
            Passed: 0,
            Failed: 1,
            Skipped: 0,
            Failures: [],
            FailureEnvelope: envelope);
    }

    private static TestRunFailureEnvelopeDto ClassifyNoTrxFailure(CommandExecutionDto execution)
    {
        var stdOutTail = Tail(execution.StdOut);
        var stdErrTail = Tail(execution.StdErr);

        // MSB3027 / MSB3021: another process holds the test assembly. This is the
        // Windows concurrent-testhost collision pattern documented in ai_docs/runtime.md
        // § *Known issues*. Classify as retryable so callers (or an outer retry loop)
        // can close the conflicting runner and try again without touching source.
        //
        // Item 4: prefer EarlyKillReason when present — it proves the runner saw the pattern
        // mid-stream and killed the process, so the retry summary reflects the shortened
        // timeline instead of the full 10×1s MSBuild loop.
        var earlyKilledForFileLock = execution.EarlyKillReason is { Length: > 0 } reason
            && reason.Contains("MSB3027", StringComparison.Ordinal);
        var lockHit = earlyKilledForFileLock
            || MsBuildFileLockRegex().IsMatch(execution.StdErr)
            || MsBuildFileLockRegex().IsMatch(execution.StdOut);
        if (lockHit)
        {
            var summary = earlyKilledForFileLock
                ? $"dotnet test was terminated early (after {execution.DurationMs}ms) because MSBuild reported an MSB3027/MSB3021 file lock. " +
                  "Another process (testhost.exe, IDE test runner, or background build) is still holding the test assembly. " +
                  "Close the conflicting runner and retry the same invocation."
                : $"dotnet test exited with code {execution.ExitCode} due to an MSBuild file lock (MSB3027/MSB3021). " +
                  "Another process (testhost.exe, IDE test runner, or background build) is still holding the test assembly. " +
                  "Close the conflicting runner and retry the same invocation.";
            return new TestRunFailureEnvelopeDto(
                ErrorKind: "FileLock",
                IsRetryable: true,
                Summary: summary,
                StdOutTail: stdOutTail,
                StdErrTail: stdErrTail);
        }

        // Build failure: compilation errors prevented the test step from running.
        // Not retryable — the caller must fix the source first.
        var buildFailed = execution.StdOut.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
            || execution.StdErr.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);
        if (buildFailed)
        {
            return new TestRunFailureEnvelopeDto(
                ErrorKind: "BuildFailure",
                IsRetryable: false,
                Summary: $"dotnet test exited with code {execution.ExitCode} because the build step failed. " +
                         "Inspect the build diagnostics and fix compilation errors before retrying.",
                StdOutTail: stdOutTail,
                StdErrTail: stdErrTail);
        }

        return new TestRunFailureEnvelopeDto(
            ErrorKind: "Unknown",
            IsRetryable: false,
            Summary: $"dotnet test exited with code {execution.ExitCode} and produced no TRX output. " +
                     "Inspect StdOutTail/StdErrTail for diagnosis.",
            StdOutTail: stdOutTail,
            StdErrTail: stdErrTail);
    }

    private static string? Tail(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= FailureTailMaxChars
            ? value
            : value[^FailureTailMaxChars..];
    }

    private static (int Total, int Passed, int Failed, int Skipped, List<TestFailureDto> Failures) ParseSingleTrxFile(string trxPath)
    {
        if (!File.Exists(trxPath))
            return (0, 0, 0, 0, []);

        var document = XDocument.Load(trxPath);

        // TeamTest TRX uses the 2010 namespace, but some hosts emit the same local names under
        // a different default namespace. Match by local element name so failed tests are not
        // dropped while <Counters> still reports failed > 0 (integration: ValidationIntegrationTests).
        var counters = document.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Counters", StringComparison.Ordinal));
        var total = ParseCounter(counters, "total");
        var passed = ParseCounter(counters, "passed");
        var failed = ParseCounter(counters, "failed");
        var skipped = ParseCounter(counters, "notExecuted");

        var unitResults = document.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "UnitTestResult", StringComparison.Ordinal))
            .ToList();

        if (skipped == 0)
        {
            skipped = unitResults.Count(r =>
            {
                var outcome = (string?)r.Attribute("outcome");
                return string.Equals(outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(outcome, "Inconclusive", StringComparison.OrdinalIgnoreCase);
            });
        }

        var failures = unitResults
            .Where(result => string.Equals((string?)result.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(result =>
            {
                var fullyQualifiedName = (string?)result.Attribute("testName");
                var output = result.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Output", StringComparison.Ordinal));
                var errorInfo = output?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ErrorInfo", StringComparison.Ordinal));
                var messageEl = errorInfo?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Message", StringComparison.Ordinal));
                var stackEl = errorInfo?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "StackTrace", StringComparison.Ordinal));
                return new TestFailureDto(
                    DisplayName: GetDisplayName(fullyQualifiedName),
                    FullyQualifiedName: fullyQualifiedName,
                    Message: messageEl?.Value ?? "Test failed",
                    StackTrace: stackEl?.Value);
            })
            .ToList();

        return (total, passed, failed, skipped, failures);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static int ParseCounter(XElement? counters, string attributeName)
    {
        var value = counters?.Attribute(attributeName)?.Value;
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string GetDisplayName(string? fullyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(fullyQualifiedName))
        {
            return "unknown";
        }

        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return lastDot >= 0 ? fullyQualifiedName[(lastDot + 1)..] : fullyQualifiedName;
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;
}
