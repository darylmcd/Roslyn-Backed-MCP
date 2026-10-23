using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Reads existing native execution metadata; never evaluates a project or invokes a task factory.</summary>
internal sealed class NativeTaskExecutionIdentity
{
    internal sealed record Identity(string Context, string Implementation, string Assembly, NativeTaskHostParameters Host)
    {
        public override string ToString() => "NativeTaskExecutionIdentity";
    }

    private sealed record ProjectContext(string? Identity, ProjectInstance? Instance);
    private readonly Dictionary<string, ProjectContext> _projects = new(StringComparer.Ordinal);

    internal void Observe(BuildEventArgs message)
    {
        if (message.BuildEventContext is not { } context) return;
        var key = ProjectKey(context);
        if (message is ProjectStartedEventArgs started)
        {
            ProjectInstance? instance;
            try { instance = ReadProject(started); }
            catch (NativeTaskExecutionMetadataUnavailableException) { instance = null; }
            var identity = instance is not null && started.GlobalProperties is { } globals
                ? EscapedContextIdentity(globals, instance.ToolsVersion, CancellationToken.None) : null;
            _projects.Add(key, new(identity, instance));
        }
        else if (message is ProjectFinishedEventArgs) _projects.Remove(key);
    }

    private static ProjectInstance ReadProject(ProjectStartedEventArgs started)
    {
        var cache = Field(BuildManager.DefaultBuildManager, "_configCache") ?? throw Unavailable();
        if (cache.GetType().Assembly != typeof(ProjectInstance).Assembly
            || cache.GetType().FullName != "Microsoft.Build.BackEnd.ConfigCache") throw Unavailable();
        var index = cache.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw Unavailable();
        if (index.GetIndexParameters() is not [{ ParameterType: var parameter }] || parameter != typeof(int)) throw Unavailable();
        var configuration = index.GetValue(cache, [started.ProjectId]) ?? throw Unavailable();
        if (configuration.GetType().Assembly != typeof(ProjectInstance).Assembly
            || configuration.GetType().FullName != "Microsoft.Build.BackEnd.BuildRequestConfiguration") throw Unavailable();
        if (Field(configuration, "_project") is not ProjectInstance instance) throw Unavailable();
        if (started.ProjectFile is null || !PathEquals(instance.FullPath, started.ProjectFile)) throw Unavailable();
        var toolset = configuration.GetType().GetProperty("ToolsVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw Unavailable();
        if (toolset.PropertyType != typeof(string) || toolset.GetIndexParameters().Length != 0
            || toolset.GetValue(configuration) is not string requested || string.IsNullOrEmpty(instance.ToolsVersion)
            || !StringComparer.OrdinalIgnoreCase.Equals(started.ToolsVersion, requested)
            || !StringComparer.OrdinalIgnoreCase.Equals(requested, instance.ToolsVersion)) throw Unavailable();
        return instance;
    }

    internal Identity? Read(TaskStartedEventArgs started)
    {
        try { return ReadKnown(started); }
        catch (NativeTaskExecutionMetadataUnavailableException) { return null; }
        catch (NativeTaskRegistryUnavailableException) { return null; }
    }

    private Identity? ReadKnown(TaskStartedEventArgs started)
    {
        if (started.BuildEventContext is not { } context || !_projects.TryGetValue(ProjectKey(context), out var project)) return null;
        if (project.Instance is null || project.Identity is null) return null;
        var registry = Field(project.Instance, "_taskRegistry") ?? throw Unavailable();
        if (registry.GetType().Assembly != typeof(ProjectInstance).Assembly
            || registry.GetType().FullName != "Microsoft.Build.Execution.TaskRegistry") throw Unavailable();
        var records = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectExistingRecords(registry, records, new HashSet<object>(ReferenceEqualityComparer.Instance));
        var identities = new HashSet<Identity>();
        var unverified = false;
        foreach (var record in records)
        {
            if (record.GetType().Assembly != typeof(ProjectInstance).Assembly
                || record.GetType().FullName != "Microsoft.Build.Execution.TaskRegistry+RegisteredTaskRecord") throw Unavailable();
            var wrapper = Field(record, "_taskFactoryWrapperInstance");
            if (wrapper is null) continue;
            if (wrapper.GetType().Assembly != typeof(ProjectInstance).Assembly
                || wrapper.GetType().FullName != "Microsoft.Build.Execution.TaskFactoryWrapper") throw Unavailable();
            if (Field(wrapper, "_taskName") is not string name) throw Unavailable();
            var requestedName = CanonicalTask(started.TaskName);
            if (!CanMatch(name, started.TaskName)) continue;
            if (requestedName is null || CanonicalTask(name) != requestedName) { unverified = true; continue; }
            var factory = Field(wrapper, "_taskFactory") ?? throw Unavailable();
            // A custom factory may live in a trusted SDK DLL. Its DLL alone is
            // never evidence that the created task is an SDK implementation.
            if (factory.GetType().Assembly != typeof(ProjectInstance).Assembly
                || factory.GetType().FullName != "Microsoft.Build.BackEnd.AssemblyTaskFactory") { unverified = true; continue; }
            var hostField = wrapper.GetType().GetField("_factoryIdentityParameters", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw Unavailable();
            var host = NativeTaskHostParameters.Read(hostField.GetValue(wrapper) ?? throw Unavailable(), hostField.FieldType);
            if (!host.UsesDefaultHost) { unverified = true; continue; }
            var loaded = Field(factory, "_loadedType") ?? throw Unavailable();
            if (loaded.GetType().Assembly != typeof(TaskPropertyInfo).Assembly
                || loaded.GetType().FullName != "Microsoft.Build.Shared.LoadedType") throw Unavailable();
            var typeProperty = loaded.GetType().GetProperty("Type", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw Unavailable();
            if (typeProperty.PropertyType != typeof(Type) || typeProperty.GetIndexParameters().Length != 0) throw Unavailable();
            if (typeProperty.GetValue(loaded) is not Type type || type.FullName is not { } implementation) throw Unavailable();
            identities.Add(new(project.Identity, implementation, type.Assembly.Location,
                host with { Runtime = (host.Runtime ?? "").ToUpperInvariant(), Architecture = (host.Architecture ?? "").ToUpperInvariant() }));
        }
        return !unverified && identities.Count == 1 ? identities.Single() : null;
    }

    internal string ReadContext(TaskStartedEventArgs started) => started.BuildEventContext is { } context
        && _projects.TryGetValue(ProjectKey(context), out var project) ? project.Identity ?? "" : "";

    internal static string GlobalsIdentity(IEnumerable<KeyValuePair<string, string>> globals, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            var values = globals.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Select(pair => pair.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length) throw Unavailable();
            writer.Write(values.Length);
            foreach (var pair in values)
            {
                NativeIdentityEncoding.WriteString(writer, pair.Key.ToUpperInvariant(), ct);
                NativeIdentityEncoding.WriteString(writer, pair.Value, ct);
            }
        }
        ct.ThrowIfCancellationRequested();
        var result = Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        ct.ThrowIfCancellationRequested();
        return result;
    }

    // ProjectInstance.GlobalProperties and ProjectStartedEventArgs.GlobalProperties
    // expose MSBuild escaped values. API evaluation and command-line transport can
    // spell the same value differently (for example XML quotes as literal or %22).
    // Decode once at this explicit boundary, before hashing semantic code units.
    // A literal percent escape remains distinct: %2522 becomes %22, not a quote.
    internal static string EscapedContextIdentity(IEnumerable<KeyValuePair<string, string>> escapedGlobals, string toolsVersion, CancellationToken ct)
        => ContextIdentity(escapedGlobals.Select(pair => new KeyValuePair<string, string>(pair.Key,
            Microsoft.Build.Evaluation.ProjectCollection.Unescape(pair.Value))), toolsVersion, ct);

    internal static string ContextIdentity(IEnumerable<KeyValuePair<string, string>> globals, string toolsVersion, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(toolsVersion)) throw Unavailable();
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            NativeIdentityEncoding.WriteString(writer, toolsVersion.ToUpperInvariant(), ct);
            NativeIdentityEncoding.WriteString(writer, GlobalsIdentity(globals, ct), ct);
        }
        ct.ThrowIfCancellationRequested();
        var identity = Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        ct.ThrowIfCancellationRequested();
        return identity;
    }

    private static IEnumerable<object> Values(object? dictionary)
    {
        if (dictionary is not IEnumerable sequence) throw Unavailable();
        foreach (var pair in sequence)
        {
            if (pair is null || !pair.GetType().IsGenericType
                || pair.GetType().GetGenericTypeDefinition() != typeof(KeyValuePair<,>)) throw Unavailable();
            var value = (pair.GetType().GetProperty("Value") ?? throw Unavailable()).GetValue(pair);
            if (value is not null) yield return value;
        }
    }
    private static void AddValues(object? dictionary, HashSet<object> records)
    {
        foreach (var value in Values(dictionary)) records.Add(value);
    }
    private static void CollectExistingRecords(object registry, HashSet<object> records, HashSet<object> seen)
    {
        if (!seen.Add(registry)) return;
        if (registry.GetType().Assembly != typeof(ProjectInstance).Assembly
            || registry.GetType().FullName != "Microsoft.Build.Execution.TaskRegistry") throw Unavailable();
        AddValues(Field(registry, "_cachedTaskRecordsWithExactMatch"), records);
        foreach (var inner in Values(Field(registry, "_cachedTaskRecordsWithFuzzyMatch"))) AddValues(inner, records);
        foreach (var name in new[] { "_taskRegistrations", "_overriddenTasks" })
        {
            if (Field(registry, name) is not { } registrations) continue;
            foreach (var group in Values(registrations))
            {
                if (group is not IEnumerable entries) throw Unavailable();
                foreach (var record in entries) records.Add(record ?? throw Unavailable());
            }
        }
        if (Field(registry, "_toolset") is not { } toolset) return;
        if (toolset.GetType().Assembly != typeof(ProjectInstance).Assembly
            || toolset.GetType().FullName != "Microsoft.Build.Evaluation.Toolset") throw Unavailable();
        // Read only registries the native lookup already initialized. Calling a
        // Toolset registry getter would evaluate registrations and create proof.
        foreach (var name in new[] { "_defaultTaskRegistry", "_overrideTaskRegistry" })
            if (Field(toolset, name) is { } existing) CollectExistingRecords(existing, records, seen);
    }
    private static object? Field(object owner, string name) => (owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw Unavailable()).GetValue(owner);
    private static string ProjectKey(BuildEventContext context) => string.Join(":", context.NodeId, context.ProjectContextId, context.SubmissionId);
    internal static string? CanonicalTask(string name) => string.Equals(name, "Csc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Microsoft.CodeAnalysis.BuildTasks.Csc", StringComparison.OrdinalIgnoreCase)
            ? "Csc" : name.Equals("GetRestoreSettingsTask", StringComparison.OrdinalIgnoreCase)
                || name.Equals("NuGet.Build.Tasks.GetRestoreSettingsTask", StringComparison.OrdinalIgnoreCase)
                ? "GetRestoreSettingsTask" : NativeOutputProducerObserver.CanonicalTask(name);
    private static bool CanMatch(string registered, string requested) => string.Equals(registered, requested, StringComparison.OrdinalIgnoreCase)
        || registered.EndsWith("." + requested, StringComparison.OrdinalIgnoreCase)
        || requested.EndsWith("." + registered, StringComparison.OrdinalIgnoreCase);
    private static bool PathEquals(string left, string right) => (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .Equals(Path.GetFullPath(left), Path.GetFullPath(right));
    private static NativeTaskExecutionMetadataUnavailableException Unavailable() => new();
}
