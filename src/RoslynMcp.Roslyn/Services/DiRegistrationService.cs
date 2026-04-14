using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Scans a workspace for dependency-injection registration patterns and reports the
/// service / implementation type pairs along with their declared lifetime. Split out of
/// the legacy <c>DependencyAnalysisService</c> as part of the SRP refactor.
/// </summary>
public sealed class DiRegistrationService : IDiRegistrationService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<DiRegistrationService> _logger;

    /// <summary>
    /// di-registrations-scan-caching: full scan is ~12 s on Jellyfin (40 projects, 187
    /// registrations). Cache scan results per (workspaceId, version, projectFilter) so repeat
    /// callers don't re-walk every syntax tree. Invalidated on workspace close via
    /// <see cref="IWorkspaceManager.WorkspaceClosed"/> and on workspace version bump.
    /// </summary>
    private readonly ConcurrentDictionary<string, DiCacheEntry> _scanCache = new(StringComparer.Ordinal);

    private sealed record DiCacheEntry(int Version, ConcurrentDictionary<string, IReadOnlyList<DiRegistrationDto>> ByFilter);

    private const string UnfilteredKey = "<all>";
    private const int MaxFilterEntriesPerWorkspace = 8;

    public DiRegistrationService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        ILogger<DiRegistrationService> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
        _workspace.WorkspaceClosed += workspaceId => _scanCache.TryRemove(workspaceId, out _);
    }

    public async Task<IReadOnlyList<DiRegistrationDto>> GetDiRegistrationsAsync(
        string workspaceId, string? projectFilter, CancellationToken ct)
    {
        // di-registrations-scan-caching: serve from cache if present and the workspace hasn't
        // bumped its version. Cap entries per workspace to avoid unbounded growth on chatty
        // filter combinations.
        var version = _workspace.GetCurrentVersion(workspaceId);
        var key = projectFilter ?? UnfilteredKey;

        var entry = _scanCache.AddOrUpdate(
            workspaceId,
            _ => new DiCacheEntry(version, new ConcurrentDictionary<string, IReadOnlyList<DiRegistrationDto>>(StringComparer.OrdinalIgnoreCase)),
            (_, existing) => existing.Version == version
                ? existing
                : new DiCacheEntry(version, new ConcurrentDictionary<string, IReadOnlyList<DiRegistrationDto>>(StringComparer.OrdinalIgnoreCase)));

        if (entry.ByFilter.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        var results = await ScanProjectsAsync(workspaceId, solution, projectFilter, ct).ConfigureAwait(false);

        if (entry.ByFilter.Count >= MaxFilterEntriesPerWorkspace)
        {
            var someKey = entry.ByFilter.Keys.FirstOrDefault();
            if (someKey is not null) entry.ByFilter.TryRemove(someKey, out _);
        }
        entry.ByFilter[key] = results;
        return results;
    }

    private async Task<IReadOnlyList<DiRegistrationDto>> ScanProjectsAsync(
        string workspaceId, Solution solution, string? projectFilter, CancellationToken ct)
    {
        var results = new List<DiRegistrationDto>();
        var projects = ProjectFilterHelper.FilterProjects(solution, projectFilter);

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ct.IsCancellationRequested) break;

                SemanticModel semanticModel;
                SyntaxNode root;
                try
                {
                    semanticModel = compilation.GetSemanticModel(tree);
                    root = await tree.GetRootAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to analyze syntax tree {Path} for DI registrations, skipping",
                        tree.FilePath);
                    continue;
                }

                var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    if (TryCreateDiRegistration(invocation, semanticModel, ct, out var dto))
                        results.Add(dto);
                }
            }
        }

        return results;
    }

    private static bool TryCreateDiRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct,
        out DiRegistrationDto dto)
    {
        dto = default!;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return false;

        var receiverType = method.ReceiverType ?? method.Parameters.FirstOrDefault()?.Type;
        if (receiverType is null)
            return false;

        var isServiceCollectionMethod = receiverType.Name is "IServiceCollection"
            || receiverType.AllInterfaces.Any(i => i.Name == "IServiceCollection");

        if (!isServiceCollectionMethod && !method.ContainingType.Name.Contains("ServiceCollection", StringComparison.Ordinal))
            return false;

        var lifetime = MapDiLifetime(method.Name);
        if (lifetime is null)
            return false;

        var serviceType = "unknown";
        var implType = "unknown";

        if (method.TypeArguments.Length == 2)
        {
            serviceType = method.TypeArguments[0].ToDisplayString();
            implType = method.TypeArguments[1].ToDisplayString();
        }
        else if (method.TypeArguments.Length == 1)
        {
            serviceType = method.TypeArguments[0].ToDisplayString();
            var args = invocation.ArgumentList.Arguments;
            if (args.Count > 0 &&
                args[0].Expression is AnonymousFunctionExpressionSyntax or LambdaExpressionSyntax)
            {
                implType = "factory";
            }
            else if (args.Count > 0)
            {
                // FLAG-11B: When the argument is an instance (not a lambda factory), the
                // implementation is whatever runtime object the caller passes. Try to resolve
                // the argument's compile-time type via the semantic model and report that;
                // fall back to "instance" rather than mirroring the service type.
                implType = ResolveInstanceArgumentType(args[0].Expression, semanticModel, ct) ?? "instance";
            }
            else
            {
                implType = serviceType;
            }
        }

        if (serviceType == "unknown" &&
            TryGetDiTypesFromTypeOfArguments(invocation, semanticModel, ct, out var st, out var it))
        {
            serviceType = st;
            implType = it;
        }

        var lineSpan = invocation.GetLocation().GetLineSpan();
        dto = new DiRegistrationDto(
            serviceType,
            implType,
            lifetime,
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1,
            method.Name);
        return true;
    }

    private static string? MapDiLifetime(string methodName) => methodName switch
    {
        "AddSingleton" => "Singleton",
        "AddScoped" => "Scoped",
        "AddTransient" => "Transient",
        "AddHostedService" => "Singleton",
        "AddKeyedSingleton" => "Singleton",
        "AddKeyedScoped" => "Scoped",
        "AddKeyedTransient" => "Transient",
        _ => null
    };

    private static bool TryGetDiTypesFromTypeOfArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken ct,
        out string serviceType,
        out string implType)
    {
        serviceType = "unknown";
        implType = "unknown";
        var args = invocation.ArgumentList.Arguments;
        if (args.Count >= 2 &&
            args[0].Expression is TypeOfExpressionSyntax t0 &&
            args[1].Expression is TypeOfExpressionSyntax t1)
        {
            // FLAG-11A: typeof(X) is itself a System.Type expression — the SEMANTIC type of the
            // typeof expression is always System.Type. The actual type being captured lives in
            // the inner TypeSyntax (t0.Type). Resolve the inner type via GetTypeInfo on .Type
            // (or GetSymbolInfo for unbound generics) so we capture e.g. ILogger<>, not Type.
            var st = ResolveTypeOfArgument(t0, semanticModel, ct);
            var it = ResolveTypeOfArgument(t1, semanticModel, ct);
            if (st is not null && it is not null)
            {
                serviceType = st;
                implType = it;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// FLAG-11A: Resolve the inner type of a <c>typeof(X)</c> expression. Handles bound and
    /// unbound generic types (e.g., <c>typeof(ILogger&lt;&gt;)</c>).
    /// </summary>
    private static string? ResolveTypeOfArgument(TypeOfExpressionSyntax typeOf, SemanticModel semanticModel, CancellationToken ct)
    {
        // First try regular type info on the inner type syntax.
        var typeInfo = semanticModel.GetTypeInfo(typeOf.Type, ct);
        if (typeInfo.Type is not null && typeInfo.Type is not IErrorTypeSymbol)
        {
            return typeInfo.Type.ToDisplayString();
        }

        // For unbound generics like typeof(ILogger<>) the type info path may not resolve
        // through GetTypeInfo on the type syntax — fall back to GetSymbolInfo.
        var symbolInfo = semanticModel.GetSymbolInfo(typeOf.Type, ct);
        if (symbolInfo.Symbol is INamedTypeSymbol named)
        {
            return named.IsUnboundGenericType ? named.ToDisplayString() : named.ToDisplayString();
        }

        // Final fallback: return the syntactic text so the caller at least sees what was passed.
        return typeOf.Type.ToString();
    }

    /// <summary>
    /// FLAG-11B: For a one-generic AddSingleton/AddScoped/AddTransient overload that takes a
    /// pre-constructed instance, resolve the compile-time type of the argument expression so
    /// the implementation column is meaningful instead of being a copy of the service type.
    /// </summary>
    private static string? ResolveInstanceArgumentType(ExpressionSyntax expression, SemanticModel semanticModel, CancellationToken ct)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, ct);
        if (typeInfo.Type is not null && typeInfo.Type is not IErrorTypeSymbol)
        {
            return typeInfo.Type.ToDisplayString();
        }
        var converted = typeInfo.ConvertedType;
        if (converted is not null && converted is not IErrorTypeSymbol)
        {
            return converted.ToDisplayString();
        }
        return null;
    }
}
