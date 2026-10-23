using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Read-only snapshot of the existing native active registry; never resolves or constructs a task factory.</summary>
internal sealed class NativeTaskRegistrations : IEquatable<NativeTaskRegistrations>
{
    internal sealed record Registration(string Name, string Factory, string? AssemblyFile, string? AssemblyName,
        string Definition, ImmutableArray<string> OverrideNames, string Signature)
    {
        public override string ToString() => "NativeTaskRegistration";
    }

    private NativeTaskRegistrations(ImmutableArray<Registration> registrations, string identity, string toolsVersion)
    {
        Registrations = registrations;
        Identity = identity;
        ToolsVersion = toolsVersion;
    }

    internal ImmutableArray<Registration> Registrations { get; }
    internal string Identity { get; }
    internal string ToolsVersion { get; }
    public bool Equals(NativeTaskRegistrations? other) => other is not null && Identity == other.Identity;
    public override bool Equals(object? obj) => obj is NativeTaskRegistrations other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
    public override string ToString() => "NativeTaskRegistrations";

    internal static NativeTaskRegistrations Capture(ProjectInstance instance, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var assembly = typeof(ProjectInstance).Assembly;
        if (instance.GetType() != typeof(ProjectInstance)
            || assembly.GetName().Name != "Microsoft.Build") throw Unavailable();
        var toolsVersion = RequiredString(instance.ToolsVersion);
        var registry = Field(instance, "_taskRegistry") ?? throw Unavailable();
        if (registry.GetType().FullName != "Microsoft.Build.Execution.TaskRegistry"
            || registry.GetType().Assembly != assembly) throw Unavailable();
        var records = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in Entries(Field(registry, "_taskRegistrations"), ct))
            foreach (var record in Sequence(entry.Value, ct)) records.Add(record);
        var overrides = new Dictionary<object, List<string>>(ReferenceEqualityComparer.Instance);
        foreach (var entry in Entries(Field(registry, "_overriddenTasks"), ct))
        {
            if (entry.Key is not string name) throw Unavailable();
            foreach (var record in Sequence(entry.Value, ct))
            {
                records.Add(record);
                if (!overrides.TryGetValue(record, out var names)) overrides.Add(record, names = []);
                names.Add(name);
            }
        }
        var ordered = records.Select(record => (Record: record, Order: Field(record, "_registrationOrderId") is int order ? order : throw Unavailable()))
            .OrderBy(record => record.Order).ToArray();
        if (ordered.Select(record => record.Order).Distinct().Count() != ordered.Length) throw Unavailable();
        var result = ImmutableArray.CreateBuilder<Registration>();
        foreach (var (record, _) in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (record.GetType().FullName != "Microsoft.Build.Execution.TaskRegistry+RegisteredTaskRecord"
                || record.GetType().Assembly != assembly) throw Unavailable();
            var name = RequiredString(Field(record, "_registeredName"));
            var factory = RequiredString(Field(record, "_taskFactory"));
            var definition = RequiredString(Field(record, "_definingFileFullPath"));
            var load = Field(record, "_taskFactoryAssemblyLoadInfo") ?? throw Unavailable();
            var loadField = record.GetType().GetField("_taskFactoryAssemblyLoadInfo", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw Unavailable();
            var framework = typeof(TaskPropertyInfo).Assembly;
            var loadType = load.GetType();
            if (framework.GetName().Name != "Microsoft.Build.Framework"
                || loadField.FieldType.FullName != "Microsoft.Build.Shared.AssemblyLoadInfo"
                || loadField.FieldType.Assembly != framework
                || loadType.Assembly != framework
                || loadType.BaseType != loadField.FieldType
                || loadType.FullName is not ("Microsoft.Build.Shared.AssemblyLoadInfo+AssemblyLoadInfoWithFile"
                    or "Microsoft.Build.Shared.AssemblyLoadInfo+AssemblyLoadInfoWithName")) throw Unavailable();
            var file = StringProperty(load, "AssemblyFile");
            var assemblyName = StringProperty(load, "AssemblyName");
            if ((file is null) == (assemblyName is null)) throw Unavailable();
            var names = overrides.TryGetValue(record, out var values) ? values.Order(StringComparer.Ordinal).ToImmutableArray() : [];
            var body = Field(record, "_parameterGroupAndTaskBody") ?? throw Unavailable();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                Append(writer, name); Append(writer, factory); Append(writer, file); Append(writer, assemblyName); Append(writer, definition);
                writer.Write(names.Length);
                foreach (var value in names) Append(writer, value);
                var hostParameters = Field(record, "_taskFactoryParameters") ?? throw Unavailable();
                var hostField = record.GetType().GetField("_taskFactoryParameters", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw Unavailable();
                var host = NativeTaskHostParameters.Read(hostParameters, hostField.FieldType);
                Append(writer, host.Runtime);
                Append(writer, host.Architecture);
                Append(writer, host.DotnetHostPath);
                Append(writer, host.MSBuildAssemblyPath);
                writer.Write(host.TaskHostFactoryExplicitlyRequested.HasValue);
                if (host.TaskHostFactoryExplicitlyRequested is { } requested) writer.Write(requested);
                Append(writer, Field(body, "_inlineTaskXmlBody") as string);
                if (Field(body, "_taskBodyEvaluated") is not bool evaluated) throw Unavailable();
                writer.Write(evaluated);
                var inputs = Entries(Field(body, "_usingTaskParameters"), ct).OrderBy(pair => pair.Key as string, StringComparer.Ordinal).ToArray();
                writer.Write(inputs.Length);
                foreach (var pair in inputs)
                {
                    Append(writer, RequiredString(pair.Key));
                    if (pair.Value is not TaskPropertyInfo info) throw Unavailable();
                    Append(writer, info.Name); Append(writer, info.PropertyType.AssemblyQualifiedName);
                    writer.Write(info.Required); writer.Write(info.Output);
                }
            }
            ct.ThrowIfCancellationRequested();
            var signature = Convert.ToHexString(SHA256.HashData(stream.ToArray()));
            ct.ThrowIfCancellationRequested();
            result.Add(new(name, factory, file, assemblyName, definition, names, signature));
        }
        using var aggregate = new MemoryStream();
        using (var writer = new BinaryWriter(aggregate, Encoding.UTF8, leaveOpen: true))
        {
            NativeIdentityEncoding.WriteString(writer, toolsVersion, ct);
            writer.Write(result.Count);
            foreach (var record in result) NativeIdentityEncoding.WriteString(writer, record.Signature, ct);
        }
        ct.ThrowIfCancellationRequested();
        var identity = Convert.ToHexString(SHA256.HashData(aggregate.ToArray()));
        ct.ThrowIfCancellationRequested();
        return new(result.ToImmutable(), identity, toolsVersion);

        void Append(BinaryWriter writer, string? value)
        {
            ct.ThrowIfCancellationRequested();
            writer.Write(value is not null);
            if (value is not null) NativeIdentityEncoding.WriteString(writer, value, ct);
            ct.ThrowIfCancellationRequested();
        }
    }

    private static object? Field(object owner, string name)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? throw Unavailable();
        return field.GetValue(owner);
    }

    private static string? StringProperty(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public) ?? throw Unavailable();
        if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length != 0) throw Unavailable();
        return property.GetValue(owner) is { } value ? RequiredString(value) : null;
    }

    private static IEnumerable<DictionaryEntry> Entries(object? value, CancellationToken ct)
    {
        if (value is null) return [];
        if (value is not IDictionary dictionary) throw Unavailable();
        var result = new List<DictionaryEntry>();
        foreach (DictionaryEntry entry in dictionary) { ct.ThrowIfCancellationRequested(); result.Add(entry); }
        return result;
    }
    private static IEnumerable<object> Sequence(object? value, CancellationToken ct)
    {
        if (value is not IEnumerable items) throw Unavailable();
        foreach (var item in items) { ct.ThrowIfCancellationRequested(); yield return item ?? throw Unavailable(); }
    }
    private static string RequiredString(object? value) => value as string ?? throw Unavailable();
    private static NativeTaskRegistryUnavailableException Unavailable() => new();
}

internal sealed class NativeTaskRegistryUnavailableException() : InvalidOperationException(
    "The installed MSBuild active task registry cannot be verified. Semantic loading remains available; use a supported SDK and reload before requesting physical mutation.");
