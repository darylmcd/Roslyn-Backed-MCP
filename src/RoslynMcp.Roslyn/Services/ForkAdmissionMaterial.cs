using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

internal static class ForkAdmissionMaterial
{
    internal const string DirectoryName = ".roslynmcp-fork-admission";
    internal const string ExecutionProperty = "RoslynMcpForkExecutionRoot";
    internal const string DeadlineProperty = "RoslynMcpForkDeadlineUtc";
    internal sealed record Configuration(int Schema, string SourceRoot, string ForkRoot, string GuardTargetsPath, string TaskAssemblyPath);

    internal static Configuration Install(string sourceRoot, string forkRoot, IEnumerable<string> inventory, CancellationToken ct)
    {
        var source = PhysicalPathResolver.Resolve(sourceRoot);
        var fork = PhysicalPathResolver.Resolve(forkRoot);
        var material = Path.Combine(fork, DirectoryName);
        var taskDirectory = Path.Combine(material, "tasks");
        Directory.CreateDirectory(taskDirectory);
        var assembly = typeof(ForkAdmissionMaterial).Assembly;
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? throw InvalidOperationErrors.Internal("The installed fork admission assembly has no directory.");
        // Retained forks outlive the original tool installation. Preserve the ordinary
        // published dependency layout; task loading remains MSBuild's responsibility.
        foreach (var dependency in Directory.EnumerateFiles(assemblyDirectory, "*.dll"))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(dependency, Path.Combine(taskDirectory, Path.GetFileName(dependency)), overwrite: false);
        }
        var config = new Configuration(1, source, fork, Path.Combine(material, "guard.targets"),
            Path.Combine(taskDirectory, Path.GetFileName(assembly.Location)));
        var configPath = Path.Combine(material, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        using var resource = assembly.GetManifestResourceStream("RoslynMcp.Roslyn.Services.ForkAdmission.targets")
            ?? throw InvalidOperationErrors.Internal("The installed fork admission target resource is missing.");
        var document = XDocument.Load(resource);
        document.Root!.AddFirst(new XElement("PropertyGroup",
            new XElement("RoslynMcpForkConfig", configPath),
            new XElement("RoslynMcpForkTaskAssembly", config.TaskAssemblyPath)));
        document.Save(config.GuardTargetsPath);
        foreach (var project in inventory
            .Where(path => !ForkPathAdmission.Contains(material, path)).Distinct(FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            ForkProjectImports.Install(project, fork, config.GuardTargetsPath, ct.ThrowIfCancellationRequested);
        }
        return config;
    }

    internal static Configuration? Find(string targetPath)
        => FindContainingOwned(targetPath).LastOrDefault();

    // Physical containment is authority for retention ownership. SourceRoot is an
    // admission boundary, never a pointer granting storage or lifetime authority.
    internal static IReadOnlyList<Configuration> FindContainingOwned(string targetPath)
    {
        var owners = new List<Configuration>();
        var physical = PhysicalPathResolver.Resolve(targetPath);
        for (var directory = Directory.Exists(physical) ? physical : Path.GetDirectoryName(physical);
            directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (ReadOwnedAtDirectory(directory) is { } configuration) owners.Add(configuration);
        }
        owners.Reverse();
        return owners;
    }

    internal static Configuration? ReadOwnedAtDirectory(string directory)
    {
        var physical = PhysicalPathResolver.Resolve(directory);
        var configPath = Path.Combine(physical, DirectoryName, "config.json");
        if (!File.Exists(configPath))
        {
            var guard = Path.Combine(physical, DirectoryName, "guard.targets");
            if (!File.Exists(guard)) return null;
            var text = File.ReadAllText(guard);
            if (!text.Contains("<RoslynMcpForkConfig>", StringComparison.Ordinal) ||
                !text.Contains("RoslynMcp.Roslyn.Services.DiscoverForkTargetDependencies", StringComparison.Ordinal)) return null;
        }
        var configuration = Read(configPath);
        if (!FileSystemPath.Comparer.Equals(configuration.ForkRoot, physical))
            throw new PublicInvalidOperationException("The retained fork material does not match its physical owner. Recreate the fork before retrying.");
        return configuration;
    }

    internal static void RevalidateContainingOwned(string targetPath, IReadOnlyList<Configuration> expected)
    {
        if (!expected.SequenceEqual(FindContainingOwned(targetPath)))
            throw new PublicInvalidOperationException("The retained fork admission ancestry changed. Recreate the fork before retrying.");
    }

    internal static Configuration Read(string configPath)
    {
        try
        {
            var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(configPath));
            var material = Path.GetDirectoryName(PhysicalPathResolver.Resolve(configPath));
            var fork = material is null ? null : Path.GetDirectoryName(material);
            if (config is null || config.Schema != 1 || fork is null ||
                !FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(config.ForkRoot), fork) ||
                !FileSystemPath.Comparer.Equals(Path.GetFileName(material), DirectoryName) ||
                FileSystemPath.Comparer.Equals(PhysicalPathResolver.Resolve(config.SourceRoot), fork) ||
                !ForkPathAdmission.Contains(material!, PhysicalPathResolver.Resolve(config.GuardTargetsPath)) ||
                !ForkPathAdmission.Contains(material!, PhysicalPathResolver.Resolve(config.TaskAssemblyPath)) ||
                !File.Exists(config.GuardTargetsPath) || !File.Exists(config.TaskAssemblyPath))
                throw InvalidOperationErrors.Internal("The retained fork admission configuration is invalid.");
            return config;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PublicInvalidOperationException)
        {
            throw new PublicInvalidOperationException(
                "The retained fork admission material could not be verified. Recreate the fork before retrying.", ex);
        }
    }

    internal static void StripPreviousImports(string copiedRoot, Configuration previous, CancellationToken ct)
    {
        foreach (var path in Directory.EnumerateFiles(copiedRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            // Arbitrary-extension build entry documents are covered without rewriting
            // inert XML: inspect only files containing the exact owned target identity.
            var bytes = ForkProjectImports.ReadShared(path);
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = reader.ReadToEnd();
            if (!text.Contains(ForkProjectImports.Label, StringComparison.Ordinal)) continue;
            XDocument document;
            var isProject = false;
            try
            {
                using var probe = XmlReader.Create(new StringReader(text));
                probe.MoveToContent();
                isProject = probe.NodeType == XmlNodeType.Element && probe.LocalName == "Project";
                if (!isProject) continue;
                stream.Position = 0;
                document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException ex)
            {
                if (isProject)
                    throw new PublicInvalidOperationException("A previous owned fork import is malformed. Recreate the fork before retrying.", ex);
                continue;
            }
            if (document.Root is not { Name.LocalName: "Project" } root) continue;
            var matches = root.Elements(root.Name.Namespace + "Import")
                .Where(element => (string?)element.Attribute("Label") == ForkProjectImports.Label &&
                    FileSystemPath.Comparer.Equals((string?)element.Attribute("Project"), previous.GuardTargetsPath)).ToArray();
            foreach (var import in matches)
            {
                if (import.Attributes().Count() != 2)
                    throw new PublicInvalidOperationException("A previous owned fork import is malformed. Recreate the fork before retrying.");
                import.Remove();
            }
            if (matches.Length != 0) ForkProjectImports.Write(path, document, bytes, ct.ThrowIfCancellationRequested);
        }
    }
}

internal sealed class ForkAdmissionExecution : IDisposable
{
    private readonly CancellationTokenRegistration _registration;
    private readonly IReadOnlyList<IDisposable> _forkOwnership;
    private Exception? _cancellationWriteFailure;
    private bool _completed;
    internal string Root { get; }
    internal DateTimeOffset? Deadline { get; }
    internal ForkAdmissionMaterial.Configuration Configuration { get; }
    internal string? CleanupWarning { get; private set; }

    internal static async ValueTask<ForkAdmissionExecution> CreateAsync(ForkAdmissionMaterial.Configuration configuration, DateTimeOffset? deadline, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var waiting = deadline is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (deadline is { } waitDeadline)
        {
            var remaining = waitDeadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("Native fork admission exceeded its owning invocation deadline.");
            waiting!.CancelAfter(remaining);
        }
        var ancestry = ForkAdmissionMaterial.FindContainingOwned(configuration.ForkRoot);
        if (ancestry.LastOrDefault() != configuration)
            throw new PublicInvalidOperationException("The retained fork admission material changed. Recreate the fork before retrying.");
        IReadOnlyList<IDisposable> ownership;
        try
        {
            ownership = await WorkspaceForkApplyService.AcquireForkApplyLocksAsync(ancestry.Select(owner => owner.ForkRoot), waiting?.Token ?? ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && waiting?.IsCancellationRequested == true)
        {
            throw new TimeoutException("Native fork admission exceeded its owning invocation deadline while waiting for fork ownership.", ex);
        }
        string? root = null;
        ForkAdmissionExecution? execution = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (deadline is { } limit && DateTimeOffset.UtcNow >= limit)
                throw new TimeoutException("Native fork admission exceeded its owning invocation deadline.");
            // Expiry may have won after Find. Validate again while deletion is excluded;
            // never recreate directories from a stale configuration snapshot.
            ForkAdmissionMaterial.RevalidateContainingOwned(configuration.ForkRoot, ancestry);
            var current = ForkAdmissionMaterial.Read(Path.Combine(configuration.ForkRoot, ForkAdmissionMaterial.DirectoryName, "config.json"));
            if (current != configuration)
                throw InvalidOperationErrors.Internal("The retained fork admission configuration changed before invocation.");
            root = Path.Combine(configuration.ForkRoot, ForkAdmissionMaterial.DirectoryName, "executions", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            execution = new ForkAdmissionExecution(configuration, deadline, ct, root, ownership);
            ct.ThrowIfCancellationRequested();
            return execution;
        }
        catch (Exception primary)
        {
            execution?._registration.Dispose();
            try { if (root is not null && Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { primary.Data["ForkAdmissionCleanupWarning"] = "Unstarted native admission material could not be removed: " + cleanup.GetType().Name; }
            WorkspaceForkApplyService.ReleaseForkApplyLocks(ownership);
            throw;
        }
    }

    private ForkAdmissionExecution(ForkAdmissionMaterial.Configuration configuration, DateTimeOffset? deadline, CancellationToken ct, string root, IReadOnlyList<IDisposable> forkOwnership)
    {
        Configuration = configuration;
        Deadline = deadline;
        Root = root;
        _forkOwnership = forkOwnership;
        _registration = ct.Register(() =>
        {
            try { File.WriteAllText(Path.Combine(Root, "cancelled"), string.Empty); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Interlocked.CompareExchange(ref _cancellationWriteFailure, ex, null); }
        });
    }

    internal Dictionary<string, string> Properties(IDictionary<string, string>? original)
    {
        var result = original is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase);
        result[ForkAdmissionMaterial.ExecutionProperty] = Root;
        if (Deadline is { } deadline) result[ForkAdmissionMaterial.DeadlineProperty] = deadline.ToString("O", CultureInfo.InvariantCulture);
        else result.Remove(ForkAdmissionMaterial.DeadlineProperty);
        return result;
    }

    internal string[] Arguments(IReadOnlyList<string> original) => original.Concat(Properties(null)
        .Select(property => "-p:" + property.Key + "=" + Microsoft.Build.Evaluation.ProjectCollection.Escape(property.Value))).ToArray();

    internal void Complete() => _completed = true;

    internal void ThrowIfRefused()
    {
        if (Directory.EnumerateFiles(Root, "refused-*").Any())
            throw new PublicInvalidOperationException(
                "The copied project paths were refused before a native build effect. Make source paths fork-relative or independent before retrying.",
                InvalidOperationErrors.Internal("A native fork admission task refused this invocation."));
    }

    public void Dispose()
    {
        _registration.Dispose();
        try
        {
            if (!_completed)
            {
                CleanupWarning = "Fork admission execution material was retained because native completion is uncertain.";
                return;
            }
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CleanupWarning = "Fork admission execution material could not be removed: " + ex.GetType().Name; }
            if (_cancellationWriteFailure is not null)
                CleanupWarning = "Fork admission cancellation marker could not be written: " + _cancellationWriteFailure.GetType().Name;
        }
        finally { WorkspaceForkApplyService.ReleaseForkApplyLocks(_forkOwnership); }
    }
}
