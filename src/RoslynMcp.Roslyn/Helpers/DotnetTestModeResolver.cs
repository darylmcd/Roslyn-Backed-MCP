using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Resolves dotnet's nearest global.json only through authorized, fail-closed explicit IO.</summary>
internal static class DotnetTestModeResolver
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static Task<PreparedDotnetTestContext> ResolveAsync(string workspaceId, string startDirectory,
        IWorkspacePathPolicy pathPolicy, CancellationToken ct) =>
        ResolveCoreAsync(workspaceId, startDirectory, ImmutableDictionary<string, FileSnapshotDto>.Empty, pathPolicy, ct);

    internal static Task<PreparedDotnetTestContext> ResolveOverlayAsync(string workspaceId, string startDirectory,
        IReadOnlyDictionary<string, FileSnapshotDto> ownedOverrides, IWorkspacePathPolicy pathPolicy, CancellationToken ct) =>
        ResolveCoreAsync(workspaceId, startDirectory, ownedOverrides, pathPolicy, ct);

    private static async Task<PreparedDotnetTestContext> ResolveCoreAsync(string workspaceId, string startDirectory,
        IReadOnlyDictionary<string, FileSnapshotDto> ownedOverrides, IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        var candidates = ImmutableArray.CreateBuilder<DotnetConfigurationIdentity>();
        for (var directory = Path.GetFullPath(startDirectory); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, "global.json");
            await ApproveAsync(workspaceId, [path], pathPolicy, ct).ConfigureAwait(false);
            DotnetConfigurationIdentity identity;
            byte[]? bytes;
            if (ownedOverrides.TryGetValue(path, out var owned))
            {
                bytes = owned.OriginalText is null ? null : !owned.OriginalBytes.IsDefault ? owned.OriginalBytes.ToArray()
                    : System.Text.Encoding.UTF8.GetBytes(owned.OriginalText);
                identity = new(path, bytes is not null, bytes is null ? [] : [.. SHA256.HashData(bytes)]);
            }
            else (identity, bytes) = await ReadAsync(path, ct).ConfigureAwait(false);
            candidates.Add(identity);
            if (!identity.Exists) continue;
            try
            {
                using var document = JsonDocument.Parse(bytes!, JsonOptions);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new PublicInvalidOperationException("The authorized global.json must contain an object before test execution can be selected.");
                var mtp = false;
                if (root.TryGetProperty("test", out var test))
                {
                    if (test.ValueKind != JsonValueKind.Object)
                        throw new PublicInvalidOperationException("The authorized global.json contains invalid test settings.");
                    if (test.TryGetProperty("runner", out var runner))
                    {
                        if (runner.ValueKind != JsonValueKind.String)
                            throw new PublicInvalidOperationException("The authorized global.json contains an invalid test runner setting.");
                        var mode = runner.GetString();
                        mtp = string.Equals(mode, "Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase);
                        if (!mtp && !string.Equals(mode, "VSTest", StringComparison.OrdinalIgnoreCase))
                            throw new PublicInvalidOperationException("The authorized global.json selects an unsupported test runner.");
                    }
                }
                return new(candidates.ToImmutable(), mtp, []);
            }
            catch (JsonException)
            {
                throw new PublicInvalidOperationException("The authorized global.json is malformed. Correct it before preparing a test invocation.");
            }
        }
        return new(candidates.ToImmutable(), false, []);
    }

    internal static async Task ValidateAsync(string workspaceId, string startDirectory, PreparedDotnetTestContext context,
        IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        if (context.Candidates.IsDefaultOrEmpty)
            throw new PublicInvalidOperationException("The prepared test invocation has no authorized dotnet configuration inventory. Prepare it again.");
        if (context.ExecutionInputs.IsDefault)
            throw new PublicInvalidOperationException("The prepared invocation has no explicit execution-input inventory. Prepare it again.");
        await ApproveAsync(workspaceId, context.ExecutionInputs.Select(input => input.FilePath).ToArray(), pathPolicy, ct).ConfigureAwait(false);
        foreach (var input in context.ExecutionInputs)
        {
            var (current, _) = await ReadAsync(input.FilePath, ct).ConfigureAwait(false);
            if (input.Exists != current.Exists || !input.ContentHash.AsSpan().SequenceEqual(current.ContentHash.AsSpan()))
                throw new PublicInvalidOperationException("The prepared test execution inputs changed. Prepare the test invocation again.");
        }
        var actual = await ResolveAsync(workspaceId, startDirectory, pathPolicy, ct).ConfigureAwait(false);
        if (context.UsesNativeMtp != actual.UsesNativeMtp || context.Candidates.Length != actual.Candidates.Length
            || context.Candidates.Zip(actual.Candidates).Any(pair =>
                !FileSystemPath.Comparer.Equals(pair.First.FilePath, pair.Second.FilePath)
                || pair.First.Exists != pair.Second.Exists
                || !pair.First.ContentHash.AsSpan().SequenceEqual(pair.Second.ContentHash.AsSpan())))
            throw new PublicInvalidOperationException("The dotnet configuration changed after test preparation. Prepare the test invocation again.");
    }

    private static async Task ApproveAsync(string workspaceId, IReadOnlyList<string> paths,
        IWorkspacePathPolicy pathPolicy, CancellationToken ct)
    {
        try { await pathPolicy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false); }
        catch (UnauthorizedAccessException)
        {
            throw new PublicInvalidOperationException("The nearest global.json cannot be established within the currently authorized workspace. Place an explicit global.json inside that workspace or authorize the actual inherited configuration, then retry.");
        }
    }

    private static async Task<(DotnetConfigurationIdentity Identity, byte[]? Bytes)> ReadAsync(string path, CancellationToken ct)
    {
        WorkspaceManager.EnsurePhysicalPath(path);
        try
        {
            var bytes = await PhysicalInputReader.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return (new(path, true, [.. SHA256.HashData(bytes)]), bytes);
        }
        catch (FileNotFoundException) { return (new(path, false, []), null); }
        catch (DirectoryNotFoundException) { return (new(path, false, []), null); }
        catch (UnauthorizedAccessException)
        {
            throw new PublicInvalidOperationException("The authorized global.json could not be read. Correct its filesystem access before preparing tests.");
        }
        catch (IOException)
        {
            throw new PublicInvalidOperationException("The authorized global.json could not be read. Correct its filesystem state before preparing tests.");
        }
    }
}
