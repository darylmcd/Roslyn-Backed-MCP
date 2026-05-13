using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Item 6 (v1.18): on-disk store for <see cref="CompositePreviewStore.Entry"/> records. Lets a
/// preview created in one <c>roslynmcp</c> process be redeemed by another (multi-agent
/// orchestration). Activated when <see cref="PreviewStoreOptions.PersistDirectory"/> is set
/// (typically via <c>ROSLYNMCP_PREVIEW_PERSIST_DIR</c>).
/// </summary>
/// <remarks>
/// Layout: <c>{root}/{workspaceVersion}/{token}.json</c>. Atomic writes via
/// <c>{token}.json.tmp</c> + <c>File.Move</c>. TTL enforced at retrieve-time via the file's
/// last-write timestamp.
/// </remarks>
public sealed class PersistentCompositeStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDirectory;
    private readonly TimeSpan _ttl;
    private readonly ILogger<PersistentCompositeStorage>? _logger;

    public PersistentCompositeStorage(
        string rootDirectory,
        TimeSpan ttl,
        ILogger<PersistentCompositeStorage>? logger = null)
    {
        _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5);
        _logger = logger;
        Directory.CreateDirectory(_rootDirectory);
    }

    public void Write(string token, CompositePreviewStore.Entry entry)
    {
        var dir = Path.Combine(_rootDirectory, entry.WorkspaceVersion.ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, token + ".json");
        var tmp = path + ".tmp";

        var dto = new PersistedEntry(
            entry.WorkspaceId,
            entry.WorkspaceVersion,
            entry.Description,
            entry.Mutations.Select(m => new PersistedMutation(m.FilePath, m.UpdatedContent, m.DeleteFile)).ToArray(),
            entry.CreatedAt);

        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    public CompositePreviewStore.Entry? TryRead(string token)
    {
        // Search across workspaceVersion subdirectories — caller doesn't know the version
        // when redeeming a token from a separate process.
        if (!Directory.Exists(_rootDirectory)) return null;
        foreach (var subdir in Directory.EnumerateDirectories(_rootDirectory))
        {
            var path = Path.Combine(subdir, token + ".json");
            if (!File.Exists(path)) continue;

            // TTL check based on file write time so cross-process readers honor expiry.
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age > _ttl)
            {
                TryDelete(path);
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<PersistedEntry>(json, JsonOpts);
                if (dto is null) return null;
                return new CompositePreviewStore.Entry(
                    dto.WorkspaceId,
                    dto.WorkspaceVersion,
                    dto.Description,
                    dto.Mutations.Select(m => new CompositeFileMutation(m.FilePath, m.UpdatedContent, m.DeleteFile)).ToArray(),
                    dto.CreatedAt);
            }
            catch (Exception ex)
            {
                // Corrupt entry — drop it and return null so the in-memory miss path takes over.
                _logger?.LogDebug(ex, "PersistentCompositeStorage: dropping unreadable entry at {Path}.", path);
                TryDelete(path);
                return null;
            }
        }
        return null;
    }

    public void Delete(string token)
    {
        if (!Directory.Exists(_rootDirectory)) return;
        foreach (var subdir in Directory.EnumerateDirectories(_rootDirectory))
        {
            var path = Path.Combine(subdir, token + ".json");
            if (File.Exists(path)) TryDelete(path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup — a stale file will simply fail to deserialize on the
            // next read and be cleaned up there.
            _logger?.LogDebug(ex, "PersistentCompositeStorage: failed to delete entry at {Path}.", path);
        }
    }

    private sealed record PersistedEntry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<PersistedMutation> Mutations,
        DateTime CreatedAt);

    private sealed record PersistedMutation(string FilePath, string? UpdatedContent, bool DeleteFile);
}
