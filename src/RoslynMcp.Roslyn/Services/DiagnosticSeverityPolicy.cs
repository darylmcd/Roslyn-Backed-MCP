using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Owns diagnostic severity evaluation, analyzer eligibility probes, and filtered DTO projection.
/// </summary>
internal sealed class DiagnosticSeverityPolicy(IUnexpectedExceptionReporter? exceptionReporter)
{
    public bool ProjectHasEffectiveErrorAnalyzer(
        Project project,
        Compilation compilation,
        CancellationToken ct)
    {
        foreach (var reference in project.AnalyzerReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadProbeValues(
                    () => reference.GetAnalyzers(project.Language),
                    out ImmutableArray<DiagnosticAnalyzer> analyzers))
            {
                return true;
            }

            foreach (var analyzer in analyzers)
            {
                if (!TryReadProbeValues(
                        () => analyzer.SupportedDiagnostics,
                        out ImmutableArray<DiagnosticDescriptor> descriptors))
                {
                    return true;
                }

                foreach (var descriptor in descriptors)
                {
                    ct.ThrowIfCancellationRequested();
                    if (CanReportAsError(descriptor, compilation, ct))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static DiagnosticProjection Project(
        IEnumerable<Diagnostic> diagnostics,
        string? fileFilter,
        string? diagnosticIdFilter,
        DiagnosticSeverity? minSeverity)
    {
        var raw = new List<Diagnostic>();
        var all = new List<DiagnosticDto>();
        var filtered = new List<DiagnosticDto>();

        foreach (var diagnostic in diagnostics)
        {
            raw.Add(diagnostic);
            if (!MatchesFileFilter(diagnostic, fileFilter)
                || diagnostic.Severity == DiagnosticSeverity.Hidden
                || (diagnosticIdFilter is not null
                    && !string.Equals(
                        diagnostic.Id,
                        diagnosticIdFilter,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var dto = SymbolMapper.ToDiagnosticDto(diagnostic);
            all.Add(dto);
            if (minSeverity is null || diagnostic.Severity >= minSeverity.Value)
            {
                filtered.Add(dto);
            }
        }

        return new DiagnosticProjection(raw, all, filtered);
    }

    internal static ReportDiagnostic GetEffectiveReportDiagnostic(
        DiagnosticDescriptor descriptor,
        CompilationOptions options,
        SyntaxTree? syntaxTree,
        CancellationToken ct)
    {
        var treeOptions = options.SyntaxTreeOptionsProvider;
        if (syntaxTree is not null
            && treeOptions is not null
            && treeOptions.TryGetDiagnosticValue(syntaxTree, descriptor.Id, ct, out var treeSeverity)
            && treeSeverity != ReportDiagnostic.Default)
        {
            return treeSeverity;
        }

        if (treeOptions is not null
            && treeOptions.TryGetGlobalDiagnosticValue(descriptor.Id, ct, out var globalSeverity)
            && globalSeverity != ReportDiagnostic.Default)
        {
            return globalSeverity;
        }

        if (options.SpecificDiagnosticOptions.TryGetValue(descriptor.Id, out var specificSeverity)
            && specificSeverity != ReportDiagnostic.Default)
        {
            return specificSeverity;
        }

        if (!descriptor.IsEnabledByDefault)
        {
            return ReportDiagnostic.Suppress;
        }

        return descriptor.DefaultSeverity switch
        {
            DiagnosticSeverity.Error => ReportDiagnostic.Error,
            DiagnosticSeverity.Warning when options.GeneralDiagnosticOption == ReportDiagnostic.Error =>
                ReportDiagnostic.Error,
            DiagnosticSeverity.Warning => ReportDiagnostic.Warn,
            DiagnosticSeverity.Info => ReportDiagnostic.Info,
            DiagnosticSeverity.Hidden => ReportDiagnostic.Hidden,
            _ => ReportDiagnostic.Default,
        };
    }

    private bool TryReadProbeValues<T>(
        Func<ImmutableArray<T>> read,
        out ImmutableArray<T> values)
    {
        try
        {
            values = read();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnexpectedExceptionReporting.Report(
                exceptionReporter,
                ex,
                UnexpectedExceptionCategory.AnalyzerLoad);
            values = [];
            return false;
        }
    }

    private static bool CanReportAsError(
        DiagnosticDescriptor descriptor,
        Compilation compilation,
        CancellationToken ct)
    {
        var options = compilation.Options;
        if (GetEffectiveReportDiagnostic(descriptor, options, syntaxTree: null, ct)
            == ReportDiagnostic.Error)
        {
            return true;
        }

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            if (GetEffectiveReportDiagnostic(descriptor, options, syntaxTree, ct)
                == ReportDiagnostic.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesFileFilter(Diagnostic diagnostic, string? fileFilter)
    {
        if (fileFilter is null)
        {
            return true;
        }

        if (!diagnostic.Location.IsInSource)
        {
            return false;
        }

        var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
        return string.Equals(
            Path.GetFullPath(diagnosticPath),
            Path.GetFullPath(fileFilter),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record DiagnosticProjection(
    List<Diagnostic> Raw,
    List<DiagnosticDto> All,
    List<DiagnosticDto> Filtered);
