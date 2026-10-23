using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Streams successful native writer facts; facts alone grant no output authority.</summary>
internal sealed class NativeOutputProducerObserver(Action<CompilerReferenceCaptureFrame> send, NativeTaskExecutionIdentity identities)
{
    internal sealed record Receipt(string Project, string Target, string TargetFile, string Task, string Assembly, string Parameter, string Path,
        string ContextIdentity, string? Implementation)
    {
        public override string ToString() => "NativeOutputWriterReceipt";
    }
    private sealed record Target(string Name, string File);
    private sealed record PendingTask(string Project, string Name, string Assembly, string ContextIdentity, string? Implementation, Target Target, string Parameter)
    {
        internal string? Value { get; set; }
    }
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingTask> _tasks = new(StringComparer.Ordinal);

    internal static string? CanonicalTask(string name) => name.ToUpperInvariant() switch
    {
        "WRITECODEFRAGMENT" or "MICROSOFT.BUILD.TASKS.WRITECODEFRAGMENT" => "WriteCodeFragment",
        "WRITELINESTOFILE" or "MICROSOFT.BUILD.TASKS.WRITELINESTOFILE" => "WriteLinesToFile",
        "GENERATEMSBUILDEDITORCONFIG" or "MICROSOFT.CODEANALYSIS.BUILDTASKS.GENERATEMSBUILDEDITORCONFIG" => "GenerateMSBuildEditorConfig",
        "RESOLVEPACKAGEASSETS" or "MICROSOFT.NET.BUILD.TASKS.RESOLVEPACKAGEASSETS" => "ResolvePackageAssets",
        _ => null
    };
    internal static string? OutputParameter(string name) => CanonicalTask(name) switch
    {
        "WriteCodeFragment" => "OutputFile",
        "WriteLinesToFile" => "File",
        "GenerateMSBuildEditorConfig" => "FileName",
        "ResolvePackageAssets" => "ProjectAssetsCacheFile",
        _ => null
    };

    internal void Observe(BuildEventArgs message)
    {
        if (message.BuildEventContext is not { } context) return;
        var target = string.Join(":", context.NodeId, context.ProjectContextId, context.TargetId, context.SubmissionId, context.EvaluationId);
        var key = target + ":" + context.TaskId;
        switch (message)
        {
            case TargetStartedEventArgs targetStarted:
                _targets.Add(target, new(targetStarted.TargetName, targetStarted.TargetFile));
                break;
            case TargetFinishedEventArgs:
                _targets.Remove(target);
                break;
            case TaskStartedEventArgs taskStarted when OutputParameter(taskStarted.TaskName) is { } parameter:
                if (!_targets.TryGetValue(target, out var owner)) throw new LoggerException("Native writer target origin is unavailable.");
                var identity = identities.Read(taskStarted);
                _tasks.Add(key, new(taskStarted.ProjectFile, taskStarted.TaskName, identity?.Assembly ?? taskStarted.TaskAssemblyLocation,
                    identities.ReadContext(taskStarted), identity?.Implementation, owner, parameter));
                break;
            case TaskParameterEventArgs input when input.Kind == TaskParameterMessageKind.TaskInput && _tasks.TryGetValue(key, out var task)
                && input.ParameterName == task.Parameter:
                if (task.Value is not null || input.Items.Count != 1) throw new LoggerException("Native writer output parameter is ambiguous.");
                task.Value = input.Items[0] switch
                {
                    ITaskItem item => item.ItemSpec,
                    string text => text,
                    _ => throw new LoggerException("Native writer output representation is unavailable.")
                };
                break;
            case TaskFinishedEventArgs finished when _tasks.Remove(key, out var finishedTask):
                if (finished.Succeeded && finishedTask.Value is { Length: > 0 } value)
                    send(new("OutputWriter", key, [finishedTask.Project, finishedTask.Target.Name, finishedTask.Target.File, finishedTask.Name,
                        finishedTask.Assembly, finishedTask.Parameter, value, finishedTask.ContextIdentity, finishedTask.Implementation ?? ""]));
                break;
        }
    }
}
