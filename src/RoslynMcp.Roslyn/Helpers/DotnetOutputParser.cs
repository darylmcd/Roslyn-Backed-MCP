using System.Text.RegularExpressions;
using System.Xml;
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

    // Location-less MSBuild/NuGet form: `<file-or-tool> : error NU1201: message [project]`
    // (restore errors, `MSBUILD : error MSB1009:`). The ` : ` separator (space before the colon)
    // and the required `[A-Z]{2,}\d+` id keep it from matching the located form or ordinary prose.
    [GeneratedRegex(@"^(?<file>.+?) : (?<severity>error|warning) (?<id>[A-Z]{2,}\d+): (?<message>.+?)( \[(?<project>.+)\])?$", RegexOptions.Compiled)]
    private static partial Regex LocationlessDiagnosticRegex();

    [GeneratedRegex(@"MSB(3027|3021)", RegexOptions.Compiled)]
    private static partial Regex MsBuildFileLockRegex();

    private const int FailureTailMaxChars = 2000;

    // test-run-failures-pagination-truncation: unlike StdOut/StdErr (bounded to 12000 chars by
    // DotnetCommandRunner), TRX-parsed per-test Message/StackTrace were previously unbounded,
    // and ValidationTools.RunTests serialized every failing test with no cap on the list length.
    // A broad/unfiltered run over a large suite therefore produced a payload that grew linearly
    // with the failure count and per-failure text size.
    //
    // Measured ceiling (see RunTests_WorstCasePayload_StaysWithinTheMeasuredCeiling in
    // TestRunFailureEnvelopeTests): a source and restored-assembly audit found no repository or
    // SDK response-size constant. There is no protocol-level ceiling to point at; the real
    // ceiling is the consuming client's context budget. So the caps below are sized against that
    // budget rather than a protocol constant: with both halves of the fix in place, a worst-case
    // page (25 failures, every Message/StackTrace at the caps) measures 56,923 chars of indented
    // JSON (~14k tokens) — comparable to test_discover's own documented "1000 cases is roughly
    // 350KB" guidance. Lifting the count cap on the same 400-failure run measures 904,200 chars
    // (~883KB). Without truncation a single pathological failure is unbounded outright. Both
    // halves are load-bearing.
    //
    // Truncate from the HEAD (not the tail, unlike StdOut/StdErr) because the assertion message
    // and the throw-site frame — which come first — are the diagnostically useful part.
    private const int TestFailureMessageMaxChars = 500;
    private const int TestFailureStackTraceMaxChars = 1500;

    /// <summary>
    /// Parses MSBuild-style diagnostic lines from <c>dotnet build</c> standard output.
    /// </summary>
    /// <param name="output">The raw standard output captured from the build process.</param>
    /// <returns>
    /// A list of parsed diagnostics, including location-less lines (null line/column, null <c>Location</c>).
    /// Lines that match neither format are silently skipped.
    /// </returns>
    public static IReadOnlyList<DiagnosticDto> ParseBuildDiagnostics(string output)
    {
        var seen = new HashSet<(string Id, string File, int? Line, int? Col, string? Message)>();
        var diagnostics = new List<DiagnosticDto>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var match = DiagnosticRegex().Match(trimmed);
            var hasLocation = match.Success;
            if (!hasLocation)
            {
                match = LocationlessDiagnosticRegex().Match(trimmed);
                if (!match.Success)
                {
                    continue;
                }
            }

            var id = match.Groups["id"].Value;
            var file = match.Groups["file"].Value;
            var startLine = ParseNullableInt(match.Groups["line"].Value);
            var startColumn = ParseNullableInt(match.Groups["column"].Value);
            var endLine = ParseNullableInt(match.Groups["endLine"].Value);
            var endColumn = ParseNullableInt(match.Groups["endColumn"].Value);

            // Deduplicate by (Id, FilePath, Line, Column) to avoid MSBuild retry duplicates. A
            // location-less line has no position to tell two findings apart (e.g. NU1101 for two
            // different packages on one project), so its message joins the key.
            if (!seen.Add((id, file, startLine, startColumn, hasLocation ? null : match.Groups["message"].Value)))
                continue;

            diagnostics.Add(new DiagnosticDto(
                Id: id,
                Message: match.Groups["message"].Value,
                Severity: Capitalize(match.Groups["severity"].Value),
                Category: "Build",
                FilePath: SymbolMapper.FileOrNull(file),
                Location: SymbolMapper.ToOptionalLocationDto(
                    file, startLine, startColumn, endLine, endColumn)));
        }

        return diagnostics;
    }

    /// <summary>
    /// Parses one or more TRX files produced by <c>dotnet test</c> (solution runs may emit multiple TRX files)
    /// into a single aggregated <see cref="TestRunResultDto"/>. When the underlying command fails,
    /// the result carries a populated <see cref="TestRunFailureEnvelopeDto"/>
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
                : ClassifyExecutionFailure(execution, producedTrx: false);

            return new TestRunResultDto(
                execution,
                Total: 0,
                Passed: 0,
                Failed: execution.Succeeded ? 0 : 1,
                Skipped: 0,
                Failures: [],
                FailureEnvelope: envelope);
        }

        long total = 0;
        long passed = 0;
        long failed = 0;
        long skipped = 0;
        var failures = new List<TestFailureDto>();
        string? reportIssue = null;

        foreach (var trxPath in trxPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var one = ParseSingleTrxFile(trxPath);
            if (one.Issue is not null)
            {
                reportIssue = one.Issue;
                break;
            }
            total += one.Total;
            passed += one.Passed;
            failed += one.Failed;
            skipped += one.Skipped;
            failures.AddRange(one.Failures);
            if (total > int.MaxValue || passed > int.MaxValue || failed > int.MaxValue || skipped > int.MaxValue)
            {
                reportIssue = "Aggregated TRX counters exceed the supported range.";
                break;
            }
            reportIssue ??= one.ReportFailed ? "TRX result summary reports an unsuccessful run." : null;
        }

        var failureEnvelope = !execution.Succeeded
            ? ClassifyExecutionFailure(execution, producedTrx: true)
            : reportIssue is null ? null : ReportFailure(execution, reportIssue);
        if (total > int.MaxValue || passed > int.MaxValue || failed > int.MaxValue || skipped > int.MaxValue)
            return new TestRunResultDto(execution, 0, 0, 0, 0, [], ReportFailure(execution, reportIssue!));
        return new TestRunResultDto(execution, (int)total, (int)passed, (int)failed, (int)skipped, failures, failureEnvelope);
    }

    private static TestRunFailureEnvelopeDto ReportFailure(CommandExecutionDto execution, string issue) =>
        new("TestReportFailure", false, issue, Tail(execution.StdOut), Tail(execution.StdErr));

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

    private static TestRunFailureEnvelopeDto ClassifyExecutionFailure(
        CommandExecutionDto execution,
        bool producedTrx)
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
            Summary: $"dotnet test exited with code {execution.ExitCode} and " +
                     (producedTrx ? "produced TRX output despite the failed process. " : "produced no TRX output. ") +
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

    /// <summary>
    /// Bounds a per-test-failure <c>Message</c>/<c>StackTrace</c> value to <paramref name="maxChars"/>,
    /// keeping the HEAD — unlike <see cref="Tail"/>, which keeps the tail for command StdOut/StdErr
    /// where the final lines are the relevant ones. For an assertion message or a stack trace the
    /// start (the assertion text, the throw-site frame) is what a caller needs to diagnose the
    /// failure. Appends a marker so truncation is visible rather than silent. Returns <c>null</c>
    /// for a null or empty input so callers can apply their own fallback.
    /// </summary>
    private static string? TruncateHead(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= maxChars
            ? value
            : value[..maxChars] + TruncationMarker;
    }

    /// <summary>
    /// Appended to a head-truncated failure <c>Message</c>/<c>StackTrace</c> so consumers can tell
    /// a value was shortened. Public consumers may match on this exact suffix.
    /// </summary>
    internal const string TruncationMarker = "... [truncated]";

    private static (int Total, int Passed, int Failed, int Skipped, List<TestFailureDto> Failures, bool ReportFailed, string? Issue) ParseSingleTrxFile(string trxPath)
    {
        if (!File.Exists(trxPath))
            return (0, 0, 0, 0, [], false, "A supplied TRX report is missing.");

        XDocument document;
        try { document = XmlFileLoader.Load(trxPath); }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return (0, 0, 0, 0, [], false, "A supplied TRX report is unreadable.");
        }

        if (document.Root?.Name.LocalName != "TestRun")
            return (0, 0, 0, 0, [], false, "TRX report has no TestRun root.");
        var summaries = document.Root.Elements().Where(e => e.Name.LocalName == "ResultSummary").ToList();
        if (summaries.Count != 1)
            return (0, 0, 0, 0, [], false, "TRX report must have exactly one result summary.");
        var countersList = summaries[0].Elements().Where(e => e.Name.LocalName == "Counters").ToList();
        if (countersList.Count != 1 ||
            !TryCounter(countersList[0], "total", required: true, out var total) ||
            !TryCounter(countersList[0], "passed", required: true, out var passed) ||
            !TryCounter(countersList[0], "failed", required: true, out var failed) ||
            !TryCounter(countersList[0], "notExecuted", required: false, out var skipped) ||
            !TryCounter(countersList[0], "executed", required: false, out var executed) ||
            (long)passed + failed + skipped > total ||
            executed > total || (countersList[0].Attribute("executed") is not null && executed < (long)passed + failed))
            return (0, 0, 0, 0, [], false, "TRX report has invalid or contradictory counters.");

        var summaryOutcome = (string?)summaries[0].Attribute("outcome");
        if (string.IsNullOrWhiteSpace(summaryOutcome))
            return (0, 0, 0, 0, [], false, "TRX result summary has no outcome.");

        // TeamTest TRX uses the 2010 namespace, but some hosts emit the same local names under
        // a different default namespace. Match by local element name so failed tests are not
        // dropped while <Counters> still reports failed > 0 (integration: ValidationIntegrationTests).
        var unitResults = document.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "UnitTestResult", StringComparison.Ordinal))
            .ToList();
        if ((total > 0 && unitResults.Count == 0) || unitResults.Count > total)
            return (0, 0, 0, 0, [], false, "TRX result rows do not substantiate the reported total.");

        var resultPassed = unitResults.Count(r => string.Equals((string?)r.Attribute("outcome"), "Passed", StringComparison.OrdinalIgnoreCase));
        var resultFailed = unitResults.Count(r => string.Equals((string?)r.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase));
        var resultSkipped = unitResults.Count(r =>
            string.Equals((string?)r.Attribute("outcome"), "NotExecuted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((string?)r.Attribute("outcome"), "Inconclusive", StringComparison.OrdinalIgnoreCase));
        if (resultPassed > passed || resultFailed > failed || resultSkipped > total - passed - failed ||
            (countersList[0].Attribute("notExecuted") is not null && resultSkipped > skipped) ||
            unitResults.Any(r => string.IsNullOrWhiteSpace((string?)r.Attribute("outcome"))))
            return (0, 0, 0, 0, [], false, "TRX result outcomes contradict report counters.");
        if (countersList[0].Attribute("notExecuted") is null)
            skipped = resultSkipped;

        var definitions = document.Descendants().Where(e => e.Name.LocalName == "UnitTest")
            .GroupBy(e => (string?)e.Attribute("id"), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() == 1)
            .ToDictionary(g => g.Key!, g => g.Single(), StringComparer.Ordinal);

        var failures = unitResults
            .Where(result => string.Equals((string?)result.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(result =>
            {
                var testName = (string?)result.Attribute("testName");
                var testId = (string?)result.Attribute("testId");
                string? fullyQualifiedName = null;
                if (testId is not null && definitions.TryGetValue(testId, out var definition))
                {
                    var methods = definition.Elements().Where(e => e.Name.LocalName == "TestMethod").ToList();
                    if (methods.Count == 1)
                    {
                        var className = (string?)methods[0].Attribute("className");
                        var methodName = (string?)methods[0].Attribute("name");
                        if (!string.IsNullOrWhiteSpace(className) && !string.IsNullOrWhiteSpace(methodName))
                            fullyQualifiedName = className + "." + methodName;
                    }
                }
                var output = result.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Output", StringComparison.Ordinal));
                var errorInfo = output?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ErrorInfo", StringComparison.Ordinal));
                var messageEl = errorInfo?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Message", StringComparison.Ordinal));
                var stackEl = errorInfo?.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "StackTrace", StringComparison.Ordinal));
                return new TestFailureDto(
                    DisplayName: testId is null ? GetDisplayName(testName) : testName ?? "unknown",
                    FullyQualifiedName: fullyQualifiedName,
                    Message: TruncateHead(messageEl?.Value, TestFailureMessageMaxChars) ?? "Test failed",
                    StackTrace: TruncateHead(stackEl?.Value, TestFailureStackTraceMaxChars));
            })
            .ToList();

        var reportFailed = !string.Equals(summaryOutcome, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(summaryOutcome, "Passed", StringComparison.OrdinalIgnoreCase);
        if (failed > 0 || unitResults.Any(r =>
                !string.Equals((string?)r.Attribute("outcome"), "Passed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals((string?)r.Attribute("outcome"), "NotExecuted", StringComparison.OrdinalIgnoreCase)))
            reportFailed = true;
        return (total, passed, failed, skipped, failures, reportFailed, null);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static bool TryCounter(XElement counters, string attributeName, bool required, out int parsed)
    {
        var value = counters?.Attribute(attributeName)?.Value;
        parsed = 0;
        return value is null ? !required : int.TryParse(value, out parsed) && parsed >= 0;
    }

    private static string GetDisplayName(string? fullyQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(fullyQualifiedName))
            return "unknown";

        var lastDot = fullyQualifiedName.LastIndexOf('.');
        return lastDot >= 0 ? fullyQualifiedName[(lastDot + 1)..] : fullyQualifiedName;
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;
}
