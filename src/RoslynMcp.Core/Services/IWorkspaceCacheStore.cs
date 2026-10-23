namespace RoslynMcp.Core.Services;

/// <summary>
/// Persistent hints for project-graph coordinates and metadata-reference timestamps.
/// Consumers still load the native workspace and compare its current coordinates;
/// persisted entries never authorize paths, replace native discovery or certify compiler inputs.
/// </summary>
/// <remarks>
/// Cache key is the triple <c>(solutionHash, sdkVersion, msbuildGraphHash)</c>:
/// <list type="bullet">
///   <item>
///     <description><c>solutionHash</c> — content hash of the .sln/.slnx file at load time.
///     Entries remain untrusted even when this component matches.</description>
///   </item>
///   <item>
///     <description><c>sdkVersion</c> — typically
///     <see cref="System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription"/>.
///     This describes the running host runtime, not the selected MSBuild SDK.
///     SDK selection and compiler-input authority require independent current evidence.</description>
///   </item>
///   <item>
///     <description><c>msbuildGraphHash</c> — hash of the project graph that MSBuild evaluation
///     produced (project paths + references). Differentiates two states of the same
///     solution where a project was added/removed.</description>
///   </item>
/// </list>
///
/// <para>
/// This is internal infrastructure: not exposed as an MCP tool. The live fast path
/// is produced and consumed by <see cref="Roslyn.Services.WorkspaceManager"/>, with
/// this service owning the contract and on-disk persistence boundary.
/// </para>
///
/// <para>
/// Caches persist across restarts. Implementations must:
/// <list type="number">
///   <item>
///     <description>Tag every on-disk entry with a format-version sentinel so a future
///     format change invalidates cleanly without crashing on legacy reads.</description>
///   </item>
///   <item>
///     <description>Be idempotent on directory creation and tolerant of Windows-EPERM
///     so a failed cache write degrades to "no cache" rather than crashing the load path.</description>
///   </item>
///   <item>
///     <description>Compose the on-disk path from cache-key components in a way that
///     prevents accidental cross-key reads even when underlying serialization shapes
///     happen to coincide.</description>
///   </item>
/// </list>
/// </para>
/// </remarks>
public interface IWorkspaceCacheStore
{
    /// <summary>
    /// Reads a cache entry for the given key, or returns <see langword="null"/> on miss
    /// (key absent, format-version mismatch, corrupt file, or read-side IO failure).
    /// </summary>
    /// <param name="key">The cache key triple.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The persisted entry on hit; <see langword="null"/> on miss. Implementations MUST NOT
    /// throw for the common miss cases listed above — only an unrecoverable host-side
    /// fault should surface as an exception.
    /// </returns>
    Task<WorkspaceCacheEntry?> TryGetAsync(WorkspaceCacheKey key, CancellationToken ct);

    /// <summary>
    /// Writes a cache entry under the given key, atomically (temp file + rename).
    /// Idempotent: a re-write at the same key replaces the prior entry. On Windows-EPERM
    /// or other write-side IO failures, implementations MUST swallow the failure and
    /// return <see langword="false"/> — the consumer's expectation is "cache miss next time"
    /// not "load fails because cache write failed".
    /// </summary>
    /// <param name="key">The cache key triple.</param>
    /// <param name="entry">The entry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> on durable write; <see langword="false"/> on best-effort failure.
    /// </returns>
    Task<bool> PutAsync(WorkspaceCacheKey key, WorkspaceCacheEntry entry, CancellationToken ct);

    /// <summary>
    /// Removes the cache entry under the given key, if present. Idempotent: a call against
    /// a missing key is a no-op. Best-effort: IO failure is swallowed.
    /// </summary>
    /// <param name="key">The cache key triple.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateAsync(WorkspaceCacheKey key, CancellationToken ct);
}

/// <summary>
/// Lookup key for persisted workspace hints; matching keys do not establish snapshot identity.
/// </summary>
/// <param name="SolutionHash">
/// Content hash of the solution file (.sln or .slnx). Stable across timestamp-only updates;
/// any changed file bytes change this input to the lookup key.
/// </param>
/// <param name="SdkVersion">
/// Identifier of the running host runtime that produced the cached hints. Use
/// <see cref="System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription"/>
/// or equivalent. The selected MSBuild SDK is verified independently by the native loader.
/// </param>
/// <param name="MsbuildGraphHash">
/// Hash of the evaluated MSBuild project graph (project paths + references) at load time.
/// Differentiates two same-SDK states of the same solution where a project was added/removed.
/// </param>
public sealed record WorkspaceCacheKey(string SolutionHash, string SdkVersion, string MsbuildGraphHash);

/// <summary>
/// Persisted graph and metadata timestamp hints, independently checked against a fresh native load.
/// </summary>
/// <param name="ProjectGraph">
/// The evaluated MSBuild project graph: per-project absolute paths and project-to-project
/// references. Used only for comparison with the independently loaded native graph.
/// </param>
/// <param name="MetadataReferences">
/// Per-project metadata-reference list (assembly-path + file-mtime). Used to detect
/// candidate timestamp drift after native graph/reference coordinates agree and current
/// path grants authorize the metadata reads. Timestamps do not certify executable bytes.
/// </param>
/// <param name="CapturedAtUtc">
/// UTC timestamp the snapshot was captured. Surfaced for diagnostics; not used in the
/// cache-key triple.
/// </param>
/// <remarks>
/// What is NOT cached: <see cref="Microsoft.CodeAnalysis.Compilation"/> snapshots,
/// analyzer state, semantic models or compiler authority. The native loader produces
/// the workspace independently of these persisted hints.
/// </remarks>
public sealed record WorkspaceCacheEntry(
    IReadOnlyList<CachedProjectGraphNode> ProjectGraph,
    IReadOnlyList<CachedProjectMetadataReferences> MetadataReferences,
    DateTime CapturedAtUtc);

/// <summary>
/// One node in the cached project graph: the project's absolute path and the absolute
/// paths of the projects it references via &lt;ProjectReference&gt;.
/// </summary>
/// <param name="ProjectPath">Absolute path to the .csproj/.fsproj/.vbproj.</param>
/// <param name="ProjectReferences">Absolute paths to projects this one references.</param>
public sealed record CachedProjectGraphNode(
    string ProjectPath,
    IReadOnlyList<string> ProjectReferences);

/// <summary>
/// Per-project list of metadata references (assemblies on disk that the project pulls in
/// via package, framework, or path reference) plus their last-modified timestamps. The
/// timestamps let consumers detect "an upstream NuGet was rebuilt locally" between cache
/// write and cache read.
/// </summary>
/// <param name="ProjectPath">Absolute path to the project this list belongs to.</param>
/// <param name="References">Reference paths and file-mtimes (UTC).</param>
public sealed record CachedProjectMetadataReferences(
    string ProjectPath,
    IReadOnlyList<CachedMetadataReference> References);

/// <summary>
/// One metadata-reference entry: the absolute assembly path and the file's last-write
/// timestamp at cache-write time. Consumers compare current mtime to detect drift.
/// </summary>
/// <param name="AssemblyPath">Absolute path to the .dll on disk.</param>
/// <param name="LastWriteTimeUtc">File's last-write timestamp (UTC) at cache-write time.</param>
public sealed record CachedMetadataReference(
    string AssemblyPath,
    DateTime LastWriteTimeUtc);
