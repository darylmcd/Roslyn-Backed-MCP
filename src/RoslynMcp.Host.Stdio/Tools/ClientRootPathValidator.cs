using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Validates requested workspace paths against the server's configured sanctioned-root boundary.
/// </summary>
/// <remarks>
/// The configured boundary is always authoritative. Optional request-scoped roots can narrow that
/// boundary for a compatibility adapter, but can never widen it or replace it. An empty configured
/// boundary rejects access unless <see cref="SecurityOptions.PathValidationFailOpen"/> is explicitly
/// enabled. Symlinks and junctions are resolved component-by-component before comparison.
/// </remarks>
internal static class ClientRootPathValidator
{
    /// <summary>
    /// Verifies that <paramref name="path"/> is located under a configured sanctioned root.
    /// </summary>
    /// <param name="server">The active server, used only to resolve registered security options.</param>
    /// <param name="path">The file-system path to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="securityOptions">Explicit options override, primarily for direct tests.</param>
    /// <param name="expandSanctionedRoots">
    /// Requests acceptance under the immediate parent of a configured root. This takes effect only
    /// when the server-owned <see cref="SecurityOptions.AllowRootExpansion"/> option is also true;
    /// filesystem roots themselves are never widened.
    /// </param>
    /// <param name="narrowingRootPaths">
    /// Optional request-scoped roots supplied by a compatibility adapter. When present, a path must
    /// be inside both the configured boundary and this list. The narrowing list is never widened.
    /// </param>
    /// <returns>
    /// The boundary-canonicalized (fully link-resolved) form of <paramref name="path"/>. Callers that
    /// go on to write to the validated location MUST persist to this returned value rather than to the
    /// client-supplied string: re-walking the original path at write time re-resolves every symlink and
    /// junction component, so an attacker who swaps a link between validation and write can redirect
    /// the bytes outside the boundary (<c>path-boundary-link-swap-toctou</c>). Pinning the canonical
    /// target closes that validation-to-use window.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when no configured boundary exists in fail-closed mode, configuration is invalid, or
    /// the requested path falls outside the effective boundary.
    /// </exception>
    public static async Task<string> ValidatePathAgainstRootsAsync(
        McpServer? server,
        string path,
        CancellationToken ct,
        ILogger? logger = null,
        SecurityOptions? securityOptions = null,
        bool expandSanctionedRoots = false,
        IReadOnlyList<string>? narrowingRootPaths = null)
    {
        ct.ThrowIfCancellationRequested();
        var options = ConfiguredRootBoundary.ResolveOptions(server, securityOptions);
        var legacyClientRootPaths = await LegacyClientRootsNarrowingAdapter.TryGetNarrowingRootsAsync(
            server,
            ct,
            logger).ConfigureAwait(false);
        return await Task.Run(
            () => ValidatePath(
                path,
                options,
                expandSanctionedRoots && options.AllowRootExpansion,
                narrowingRootPaths,
                legacyClientRootPaths,
                logger),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates <paramref name="path"/> and returns its canonical link-resolved form.
    /// </summary>
    private static string ValidatePath(
        string path,
        SecurityOptions options,
        bool expandSanctionedRoots,
        IReadOnlyList<string>? narrowingRootPaths,
        IReadOnlyList<string>? legacyClientRootPaths,
        ILogger? logger)
    {
        var configuredRoots = ConfiguredRootBoundary.GetCanonicalRoots(options);
        if (configuredRoots.Count == 0)
        {
            // Fail-closed throws inside the helper; the fail-open branch still owes the caller a
            // usable canonical target, so resolve it here rather than returning an empty string.
            HandleMissingConfiguration(path, options.PathValidationFailOpen, logger);
            return ResolvePath(path);
        }

        var canonicalPath = ResolvePath(path);
        if (!IsPathAllowed(
                canonicalPath,
                configuredRoots,
                expandSanctionedRoots,
                narrowingRootPaths)
            || !IsPathAllowed(
                canonicalPath,
                configuredRoots,
                expandSanctionedRoots,
                legacyClientRootPaths))
        {
            throw new ArgumentException(
                $"Path '{path}' is outside the configured sanctioned-root boundary.",
                nameof(path));
        }

        return canonicalPath;
    }

    private static bool IsPathAllowed(
        string canonicalPath,
        IReadOnlyList<string> configuredRoots,
        bool expandSanctionedRoots,
        IReadOnlyList<string>? narrowingRootPaths)
    {
        IReadOnlyList<string>? narrowingRoots = null;
        if (narrowingRootPaths is not null)
        {
            narrowingRoots = ConfiguredRootBoundary.GetCanonicalRoots(new SecurityOptions
            {
                SanctionedRoots = narrowingRootPaths,
            });
        }

        return ConfiguredRootBoundary.IsPathAllowed(
            canonicalPath,
            configuredRoots,
            expandSanctionedRoots,
            narrowingRoots);
    }

    private static void HandleMissingConfiguration(
        string path,
        bool failOpen,
        ILogger? logger)
    {
        if (failOpen)
        {
            logger?.LogWarning(
                "No sanctioned roots are configured for path '{Path}' — allowing access because " +
                "fail-open is explicitly enabled",
                path);
            return;
        }

        logger?.LogWarning(
            "No sanctioned roots are configured for path '{Path}' — rejecting access (fail-closed)",
            path);
        throw new ArgumentException(
            $"Path validation failed for '{path}': no sanctioned roots are configured. " +
            "Configure ROSLYNMCP_SANCTIONED_ROOTS or explicitly set " +
            "ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true for compatibility.",
            nameof(path));
    }

    internal static Task<string> ResolvePathAsync(string path, CancellationToken ct) =>
        Task.Run(() => ResolvePath(path), ct);

    internal static bool IsPathUnderAnyRoot(
        string fullPath,
        IReadOnlyList<string> rootPaths,
        bool expandSanctionedRoots) =>
        ConfiguredRootBoundary.IsPathUnderAnyRoot(fullPath, rootPaths, expandSanctionedRoots);

    internal static string ResolvePath(string path) =>
        ConfiguredRootBoundary.ResolvePath(path);
}
