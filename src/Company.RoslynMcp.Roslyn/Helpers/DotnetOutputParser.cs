using System.Text.RegularExpressions;
using System.Xml.Linq;
using Company.RoslynMcp.Core.Models;

namespace Company.RoslynMcp.Roslyn.Helpers;

internal static partial class DotnetOutputParser
{
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)(,(?<endLine>\d+),(?<endColumn>\d+))?\): (?<severity>error|warning) (?<id>[A-Z]{2,}\d+): (?<message>.+?)( \[(?<project>.+)\])?$", RegexOptions.Compiled)]
    private static partial Regex DiagnosticRegex();

    public static IReadOnlyList<DiagnosticDto> ParseBuildDiagnostics(string output)
    {
        var diagnostics = new List<DiagnosticDto>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DiagnosticRegex().Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            diagnostics.Add(new DiagnosticDto(
                Id: match.Groups["id"].Value,
                Message: match.Groups["message"].Value,
                Severity: Capitalize(match.Groups["severity"].Value),
                Category: "Build",
                FilePath: match.Groups["file"].Value,
                StartLine: ParseNullableInt(match.Groups["line"].Value),
                StartColumn: ParseNullableInt(match.Groups["column"].Value),
                EndLine: ParseNullableInt(match.Groups["endLine"].Value),
                EndColumn: ParseNullableInt(match.Groups["endColumn"].Value)));
        }

        return diagnostics;
    }

    public static TestRunResultDto ParseTestRun(
        CommandExecutionDto execution,
        string trxPath)
    {
        if (!File.Exists(trxPath))
        {
            return new TestRunResultDto(
                execution,
                Total: 0,
                Passed: 0,
                Failed: execution.Succeeded ? 0 : 1,
                Skipped: 0,
                Failures: []);
        }

        var document = XDocument.Load(trxPath);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        var counters = document.Descendants(ns + "Counters").FirstOrDefault();
        var total = ParseCounter(counters, "total");
        var passed = ParseCounter(counters, "passed");
        var failed = ParseCounter(counters, "failed");
        var skipped = ParseCounter(counters, "notExecuted");

        var failures = document.Descendants(ns + "UnitTestResult")
            .Where(result => string.Equals((string?)result.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(result =>
            {
                var fullyQualifiedName = (string?)result.Attribute("testName");
                var output = result.Element(ns + "Output");
                var errorInfo = output?.Element(ns + "ErrorInfo");
                return new TestFailureDto(
                    DisplayName: GetDisplayName(fullyQualifiedName),
                    FullyQualifiedName: fullyQualifiedName,
                    Message: errorInfo?.Element(ns + "Message")?.Value ?? "Test failed",
                    StackTrace: errorInfo?.Element(ns + "StackTrace")?.Value);
            })
            .ToList();

        return new TestRunResultDto(execution, total, passed, failed, skipped, failures);
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
