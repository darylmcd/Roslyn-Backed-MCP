using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class ScriptingService : IScriptingService
{
    private readonly ILogger<ScriptingService> _logger;

    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text",
        "System.Text.RegularExpressions",
        "System.Threading.Tasks",
        "System.IO"
    ];

    private static readonly int TimeoutSeconds =
        int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS"), out var t) ? t : 10;

    public ScriptingService(ILogger<ScriptingService> logger) => _logger = logger;

    public async Task<ScriptEvaluationDto> EvaluateAsync(string code, string[]? imports, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var allImports = DefaultImports.Concat(imports ?? []).Distinct().ToArray();

        var options = ScriptOptions.Default
            .WithImports(allImports)
            .WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(System.Text.RegularExpressions.Regex).Assembly)
            .WithEmitDebugInformation(false);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            var result = await CSharpScript.EvaluateAsync<object?>(
                code, options, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            sw.Stop();
            return new ScriptEvaluationDto(
                Success: true,
                ResultType: result?.GetType().FullName,
                ResultValue: FormatResult(result),
                Error: null,
                CompilationErrors: null,
                ElapsedMs: sw.ElapsedMilliseconds,
                AppliedScriptTimeoutSeconds: TimeoutSeconds);
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            var compilationErrors = ex.Diagnostics
                .Where(d => d.Severity != DiagnosticSeverity.Hidden)
                .Select(d =>
                {
                    var lineSpan = d.Location.GetMappedLineSpan();
                    return new DiagnosticDto(
                        Id: d.Id,
                        Message: d.GetMessage(),
                        Severity: d.Severity.ToString(),
                        Category: d.Descriptor.Category,
                        FilePath: null,
                        StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
                        StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
                        EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
                        EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null);
                })
                .ToList();

            return new ScriptEvaluationDto(
                Success: false,
                ResultType: null,
                ResultValue: null,
                Error: ex.Message,
                CompilationErrors: compilationErrors,
                ElapsedMs: sw.ElapsedMilliseconds,
                AppliedScriptTimeoutSeconds: TimeoutSeconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ScriptEvaluationDto(
                Success: false,
                ResultType: null,
                ResultValue: null,
                Error: $"Script execution timed out after {TimeoutSeconds} seconds. Set ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS to increase.",
                CompilationErrors: null,
                ElapsedMs: sw.ElapsedMilliseconds,
                AppliedScriptTimeoutSeconds: TimeoutSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return new ScriptEvaluationDto(
                Success: false,
                ResultType: null,
                ResultValue: null,
                Error: $"Runtime error: {ex.GetType().Name}: {ex.Message}",
                CompilationErrors: null,
                ElapsedMs: sw.ElapsedMilliseconds,
                AppliedScriptTimeoutSeconds: TimeoutSeconds);
        }
    }

    private static string? FormatResult(object? result)
    {
        if (result is null) return "null";

        var str = result.ToString();
        if (str is not null && str.Length > 4096)
            str = str[..4093] + "...";

        return str;
    }
}
