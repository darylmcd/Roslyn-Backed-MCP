using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Enumerates every <c>catch</c> clause across the workspace whose declared exception type is
/// assignable from a caller-supplied exception type. Supports the <c>trace_exception_flow</c>
/// tool used by error-handling refactors to discover handling sites (which
/// <c>find_references</c> does not surface because reference search returns usage sites, not
/// handling sites).
/// </summary>
public sealed class ExceptionFlowService : IExceptionFlowService
{
    /// <summary>Default cap on returned catch sites when the caller does not specify <c>maxResults</c>.</summary>
    public const int DefaultMaxResults = 200;

    /// <summary>Upper bound on <c>maxResults</c> regardless of the caller's request, to keep the payload bounded.</summary>
    public const int AbsoluteMaxResults = 2000;

    /// <summary>Maximum characters of catch-body source text included in each result excerpt.</summary>
    public const int BodyExcerptLength = 200;

    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<ExceptionFlowService> _logger;

    public ExceptionFlowService(IWorkspaceManager workspace, ILogger<ExceptionFlowService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExceptionFlowResult> TraceExceptionFlowAsync(
        string workspaceId,
        string exceptionTypeMetadataName,
        string? scopeProjectFilter,
        int? maxResults,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(exceptionTypeMetadataName);

        var cap = NormalizeMaxResults(maxResults);
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projects = ProjectFilterHelper.FilterProjects(solution, scopeProjectFilter).ToList();

        var sites = new List<ExceptionCatchSiteDto>();
        string? resolvedDisplayName = null;
        var truncated = false;

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested || sites.Count >= cap) break;

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) continue;

            // Resolve the target exception type from THIS compilation. A given metadata name
            // (e.g. System.Text.Json.JsonException) may not resolve in every project's reference
            // set — we only attempt assignability against projects where we can resolve it.
            var targetType = compilation.GetTypeByMetadataName(exceptionTypeMetadataName);
            if (targetType is null) continue;

            // Remember the first successful resolution so the response can echo the display name
            // back to the caller for sanity-checking.
            resolvedDisplayName ??= targetType.ToDisplayString();

            // The catch-matching algorithm uses Roslyn's conversion API: a catch of type T
            // handles any thrown exception of type S where S is assignable to T. So we want
            // every catch whose declared type is a BASE of (or equal to) the traced type —
            // because such a catch would handle a thrown instance of the traced type.
            var systemException = compilation.GetTypeByMetadataName("System.Exception");

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested || sites.Count >= cap) break;
                if (PathFilter.IsGeneratedOrContentFile(tree.FilePath)) continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

                    foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
                    {
                        if (ct.IsCancellationRequested || sites.Count >= cap)
                        {
                            truncated = sites.Count >= cap;
                            break;
                        }

                        var site = TryBuildCatchSite(catchClause, semanticModel, targetType, systemException, ct);
                        if (site is not null)
                        {
                            sites.Add(site);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Failed to walk catch clauses in {Path}; skipping",
                        tree.FilePath);
                }
            }
        }

        if (!truncated && sites.Count >= cap)
        {
            truncated = true;
        }

        return new ExceptionFlowResult(
            exceptionTypeMetadataName,
            resolvedDisplayName,
            sites.Count,
            truncated,
            sites);
    }

    private static int NormalizeMaxResults(int? requested)
    {
        if (requested is null) return DefaultMaxResults;
        var value = requested.Value;
        if (value <= 0) return DefaultMaxResults;
        return value > AbsoluteMaxResults ? AbsoluteMaxResults : value;
    }

    private static ExceptionCatchSiteDto? TryBuildCatchSite(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? systemException,
        CancellationToken ct)
    {
        // Resolve the declared catch type. An untyped `catch { }` has no Declaration node and
        // is treated as catching System.Exception (CLR semantics: CLS-compliant thrown objects
        // are System.Exception subtypes).
        ITypeSymbol? declaredType = null;
        if (catchClause.Declaration is { Type: { } typeSyntax })
        {
            declaredType = semanticModel.GetTypeInfo(typeSyntax, ct).Type;
        }
        else
        {
            declaredType = systemException;
        }

        if (declaredType is null) return null;

        // A catch of type T handles a thrown instance of S when S is assignable to T (S is T
        // or a subtype). We want every catch where targetType is assignable to declaredType.
        if (!IsAssignableTo(targetType, declaredType)) return null;

        var catchesBase = !SymbolEqualityComparer.Default.Equals(declaredType, targetType);
        var lineSpan = catchClause.GetLocation().GetLineSpan();

        var containingMethod = GetContainingMethodDisplay(catchClause, semanticModel, ct);
        var declaredTypeMetadataName = GetMetadataName(declaredType);

        var hasFilter = catchClause.Filter is not null;
        var bodyExcerpt = BuildBodyExcerpt(catchClause);
        var rethrowAs = TryResolveRethrowAsType(catchClause, semanticModel, declaredType, ct);

        return new ExceptionCatchSiteDto(
            FilePath: lineSpan.Path,
            Line: lineSpan.StartLinePosition.Line + 1,
            ContainingMethod: containingMethod,
            DeclaredExceptionTypeMetadataName: declaredTypeMetadataName,
            CatchesBaseException: catchesBase,
            HasFilter: hasFilter,
            BodyExcerpt: bodyExcerpt,
            RethrowAsTypeMetadataName: rethrowAs);
    }

    /// <summary>
    /// Walks <paramref name="source"/>'s base-type chain looking for <paramref name="target"/>.
    /// Equivalent to <c>target.IsAssignableFrom(source)</c>: the catch's declared type
    /// <paramref name="target"/> handles a throw of <paramref name="source"/> if source derives
    /// from (or equals) target. Covers <c>System.Exception</c> as the universal base.
    /// </summary>
    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        for (var current = (ITypeSymbol?)source; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target)) return true;
        }
        return false;
    }

    private static string GetMetadataName(ITypeSymbol type)
    {
        // Prefer the fully qualified metadata name with namespaces; fall back to display string
        // when the type isn't a named type (e.g., error type after a missing reference).
        if (type is INamedTypeSymbol named)
        {
            var ns = named.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrEmpty(ns) && ns != "<global namespace>")
            {
                return $"{ns}.{named.MetadataName}";
            }
            return named.MetadataName;
        }
        return type.ToDisplayString();
    }

    private static string? GetContainingMethodDisplay(SyntaxNode node, SemanticModel model, CancellationToken ct)
    {
        var ancestor = node.Ancestors().FirstOrDefault(static a =>
            a is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or DestructorDeclarationSyntax
            or AccessorDeclarationSyntax
            or LocalFunctionStatementSyntax
            or OperatorDeclarationSyntax
            or ConversionOperatorDeclarationSyntax);
        if (ancestor is null) return null;
        var symbol = model.GetDeclaredSymbol(ancestor, ct);
        return symbol?.ToDisplayString();
    }

    /// <summary>
    /// Build the excerpt string. Per the plan, include the <c>when</c> filter source so the
    /// agent can see what actually matches without opening the file, plus the first portion
    /// of the catch body. Normalize whitespace so agents see a compact one-line preview.
    /// </summary>
    private static string BuildBodyExcerpt(CatchClauseSyntax catchClause)
    {
        var filterText = catchClause.Filter?.ToString();
        var bodyText = catchClause.Block.ToString();

        var combined = string.IsNullOrEmpty(filterText)
            ? bodyText
            : $"{filterText} {bodyText}";

        combined = NormalizeWhitespace(combined);

        if (combined.Length > BodyExcerptLength)
        {
            combined = combined.Substring(0, BodyExcerptLength);
        }
        return combined;
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var buffer = new System.Text.StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    buffer.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                buffer.Append(ch);
                previousWasWhitespace = false;
            }
        }
        return buffer.ToString().Trim();
    }

    /// <summary>
    /// Scans the catch body for <c>throw new X(...)</c> statements where <c>X</c> differs from
    /// the declared catch type. A bare <c>throw;</c> (rethrow) is not a translation and
    /// returns <see langword="null"/>. Returns the metadata name of the first different-type
    /// throw discovered (linear scan; deep bodies are rare and the first translation is the
    /// most informative).
    /// </summary>
    private static string? TryResolveRethrowAsType(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        ITypeSymbol declaredType,
        CancellationToken ct)
    {
        foreach (var throwExpr in catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            if (ct.IsCancellationRequested) return null;
            if (throwExpr.Expression is not ObjectCreationExpressionSyntax objectCreation) continue;

            var thrownType = semanticModel.GetTypeInfo(objectCreation, ct).Type;
            if (thrownType is null) continue;
            if (SymbolEqualityComparer.Default.Equals(thrownType, declaredType)) continue;

            return GetMetadataName(thrownType);
        }

        // Also check expression-bodied throws: `throw new X();` as an expression — used inside
        // expression-bodied lambdas. Catches rethrows from switch-expression arms, etc.
        foreach (var throwExpr in catchClause.Block.DescendantNodes().OfType<ThrowExpressionSyntax>())
        {
            if (ct.IsCancellationRequested) return null;
            if (throwExpr.Expression is not ObjectCreationExpressionSyntax objectCreation) continue;

            var thrownType = semanticModel.GetTypeInfo(objectCreation, ct).Type;
            if (thrownType is null) continue;
            if (SymbolEqualityComparer.Default.Equals(thrownType, declaredType)) continue;

            return GetMetadataName(thrownType);
        }

        return null;
    }
}
