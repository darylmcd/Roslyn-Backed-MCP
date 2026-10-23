using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Build.Framework;

namespace RoslynMcp.Roslyn.Services;

/// <summary>MSBuild child-process logger streaming actual Csc inputs and successful native writer facts through private IPC.</summary>
public sealed class CompilerReferenceCaptureLogger : ILogger
{
    private readonly object _gate = new();
    private readonly HashSet<string> _tasks = new(StringComparer.Ordinal);
    private BinaryWriter? _writer;
    private NativeOutputProducerObserver? _outputs;
    private NativeRestoreSettingsObserver? _restoreSettings;
    private readonly NativeTaskExecutionIdentity _identities = new();
    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Diagnostic;
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Parameters)) throw new LoggerException("Compiler reference channel is not configured.");
            var pipe = new NamedPipeClientStream(".", Parameters, PipeDirection.Out, PipeOptions.None);
            try { pipe.Connect(0); }
            catch { pipe.Dispose(); throw; }
            _writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: false);
            if (eventSource is not IEventSource3 source)
                throw new LoggerException("Compiler task input events are unavailable.");
            source.IncludeTaskInputs();
            _outputs = new NativeOutputProducerObserver(Send, _identities);
            _restoreSettings = new NativeRestoreSettingsObserver(Send, _identities);
            eventSource.AnyEventRaised += OnEvent;
        }
        catch (Exception)
        {
            // Never echo native logger parameters, task inputs or captured properties.
            throw new LoggerException("Compiler reference channel could not be opened.");
        }
    }

    private void OnEvent(object sender, BuildEventArgs message)
    {
        try
        {
            lock (_gate)
            {
                _identities.Observe(message);
                _outputs?.Observe(message);
                _restoreSettings?.Observe(message);
                switch (message)
                {
                    case TaskStartedEventArgs started when NativeTaskExecutionIdentity.CanonicalTask(started.TaskName) == "Csc":
                        {
                            var key = Context(started.BuildEventContext);
                            var identity = _identities.Read(started);
                            if (identity is null)
                            {
                                Send(new("CompilerUnavailable", key, [started.ProjectFile, _identities.ReadContext(started)]));
                                break;
                            }
                            if (!_tasks.Add(key)) throw new LoggerException("Duplicate compiler task context.");
                            Send(new("Task", key, [started.ProjectFile, identity.Assembly, identity.Context, identity.Implementation]));
                            break;
                        }
                    case TaskParameterEventArgs parameter when parameter.Kind == TaskParameterMessageKind.TaskInput
                        && parameter.ParameterName == "References" && _tasks.Contains(Context(parameter.BuildEventContext)):
                        {
                            var key = Context(parameter.BuildEventContext);
                            Send(new("References", key, [parameter.Items.Count.ToString(CultureInfo.InvariantCulture)]));
                            foreach (var value in parameter.Items)
                            {
                                if (value is not ITaskItem item) throw new LoggerException("Unsupported compiler reference input representation.");
                                Send(new("Reference", key, [item.ItemSpec, item.GetMetadata("OriginalPath"),
                                item.GetMetadata("MSBuildSourceProjectFile"), item.GetMetadata("Aliases"), item.GetMetadata("EmbedInteropTypes")]));
                            }
                            break;
                        }
                    case TaskParameterEventArgs parameter when parameter.Kind == TaskParameterMessageKind.TaskInput
                        && parameter.ParameterName == "OutputAssembly" && _tasks.Contains(Context(parameter.BuildEventContext)):
                        {
                            if (parameter.Items.Count != 1) throw new LoggerException("Compiler output identity is unavailable.");
                            Send(new("Output", Context(parameter.BuildEventContext), [Item(parameter.Items[0])]));
                            break;
                        }
                    case TaskParameterEventArgs parameter when parameter.Kind == TaskParameterMessageKind.TaskOutput
                        && parameter.ParameterName == "CommandLineArgs" && _tasks.Contains(Context(parameter.BuildEventContext)):
                        {
                            var key = Context(parameter.BuildEventContext);
                            Send(new("Arguments", key, [parameter.Items.Count.ToString(CultureInfo.InvariantCulture)]));
                            foreach (var value in parameter.Items)
                            {
                                if (value is not ITaskItem2 item)
                                    throw new LoggerException("Unsupported compiler argument output representation.");
                                // Csc wraps compiler tokens in TaskItem without escaping them.
                                // ItemSpec would decode literal percent sequences a second time.
                                Send(new("Argument", key, [item.EvaluatedIncludeEscaped]));
                            }
                            break;
                        }
                    case TaskFinishedEventArgs finished when _tasks.Contains(Context(finished.BuildEventContext)):
                        Send(new("Finished", Context(finished.BuildEventContext), [finished.Succeeded ? "true" : "false"]));
                        break;
                }
            }
        }
        catch (Exception)
        {
            throw new LoggerException("Compiler reference input capture failed.");
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            try { Send(new("Completed", "", [])); }
            catch (Exception) { throw new LoggerException("Compiler reference channel completion failed."); }
            finally { _writer?.Dispose(); _writer = null; }
        }
    }

    private static string Item(object? value) => value switch
    {
        ITaskItem item => item.ItemSpec,
        string text => text,
        _ => throw new LoggerException("Unsupported compiler parameter representation.")
    };

    private void Send(CompilerReferenceCaptureFrame frame)
    {
        var writer = _writer ?? throw new LoggerException("Compiler reference channel is not open.");
        var bytes = frame.Encode(CancellationToken.None);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        writer.Flush();
    }

    private static string Context(BuildEventContext? context)
    {
        if (context is null) throw new LoggerException("Compiler reference event has no execution context.");
        return string.Join(":", context.NodeId, context.ProjectContextId,
            context.TargetId, context.TaskId, context.SubmissionId, context.EvaluationId);
    }
}
