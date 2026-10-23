using System.Globalization;
using Microsoft.Build.Framework;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Captures the actual SDK NuGet settings task; its discovery output grants no filesystem authority.</summary>
internal sealed class NativeRestoreSettingsObserver(Action<CompilerReferenceCaptureFrame> send, NativeTaskExecutionIdentity identities)
{
    internal sealed record Receipt(string Project, string Assembly, string Context, string Implementation,
        IReadOnlyDictionary<string, string[]> Parameters)
    {
        public override string ToString() => "NativeRestoreSettingsReceipt";
    }
    private sealed record Pending(string Project, NativeTaskExecutionIdentity.Identity? Identity)
    {
        internal Dictionary<string, string[]> Parameters { get; } = new(StringComparer.Ordinal);
    }
    private readonly Dictionary<string, Pending> _tasks = new(StringComparer.Ordinal);
    internal void Observe(BuildEventArgs message)
    {
        if (message.BuildEventContext is not { } context) return;
        var key = string.Join(":", context.NodeId, context.ProjectContextId, context.TargetId, context.TaskId, context.SubmissionId, context.EvaluationId);
        switch (message)
        {
            case TaskStartedEventArgs started when NativeTaskExecutionIdentity.CanonicalTask(started.TaskName) == "GetRestoreSettingsTask":
                _tasks.Add(key, new(started.ProjectFile, identities.Read(started)));
                break;
            case TaskParameterEventArgs parameter when _tasks.TryGetValue(key, out var pending)
                && parameter.Kind is TaskParameterMessageKind.TaskInput or TaskParameterMessageKind.TaskOutput:
                var values = parameter.Items.Cast<object>().Select(value => value switch
                {
                    ITaskItem item => item.ItemSpec,
                    string text => text,
                    _ => throw new LoggerException("Native restore settings parameter representation is unavailable.")
                }).ToArray();
                if (parameter.Kind == TaskParameterMessageKind.TaskOutput && !string.IsNullOrEmpty(parameter.PropertyName))
                {
                    if (values.Length != 1 || pending.Identity is not { } producer)
                        throw new LoggerException("Native settings property output identity is unavailable.");
                    var taskType = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly =>
                        !assembly.IsDynamic && FileSystemPath.Comparer.Equals(assembly.Location, producer.Assembly))?.GetType(producer.Implementation)
                        ?? throw new LoggerException("Native settings task implementation is unavailable.");
                    var outputType = taskType.GetProperty(parameter.ParameterName)?.PropertyType;
                    // MSBuild escapes scalar/array elements before binding a task
                    // output to a property, then joins array values with raw ';'.
                    values = outputType == typeof(string[]) ? values[0].Split(';', StringSplitOptions.RemoveEmptyEntries)
                        : outputType == typeof(string) ? values : throw new LoggerException("Native settings property output type is unavailable.");
                    values = values.Select(Microsoft.Build.Evaluation.ProjectCollection.Unescape).ToArray();
                }
                if (!pending.Parameters.TryAdd(parameter.ParameterName, values))
                    throw new LoggerException("Native restore settings parameter was repeated.");
                break;
            case TaskFinishedEventArgs finished when _tasks.Remove(key, out var completed):
                if (!finished.Succeeded) break;
                if (completed.Identity is { } identity)
                {
                    var configuration = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly =>
                        !assembly.IsDynamic && assembly.GetName().Name == "NuGet.Configuration"
                        && FileSystemPath.Comparer.Equals(assembly.Location, Path.Combine(Path.GetDirectoryName(identity.Assembly)!, "NuGet.Configuration.dll")))
                        ?? throw new LoggerException("The native settings library is unavailable.");
                    var names = configuration.GetType("NuGet.Configuration.Settings")?.GetField("OrderedSettingsFileNames")?.GetValue(null) as string[]
                        ?? throw new LoggerException("The native settings filename protocol is unavailable.");
                    completed.Parameters.Add("NativeSupportedConfigNames", names);
                }
                var fields = new List<string> { completed.Project, completed.Identity?.Assembly ?? "",
                    completed.Identity?.Context ?? "", completed.Identity?.Implementation ?? "" };
                foreach (var pair in completed.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    fields.Add(pair.Key);
                    fields.Add(pair.Value.Length.ToString(CultureInfo.InvariantCulture));
                    fields.AddRange(pair.Value);
                }
                send(new("RestoreSettings", key, fields.ToArray()));
                break;
        }
    }
}
