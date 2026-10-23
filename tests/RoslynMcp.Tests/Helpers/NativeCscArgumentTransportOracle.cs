namespace RoslynMcp.Tests.Helpers;

/// <summary>Records actual native task/output representations and any true command event before item decoding.</summary>
internal static class NativeCscArgumentTransportOracle
{
    internal static string Create(string root) => NativeSdkOracleAssembly.Create(root, "NativeCscArgumentTransportOracle", """
        using System;
        using System.IO;
        using System.Collections.Generic;
        using System.Linq;
        using System.Text;
        using System.Text.Json;
        using Microsoft.Build.Framework;
        public sealed class NativeCscArgumentTransportOracleLogger : ILogger
        {
            public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Diagnostic;
            public string Parameters { get; set; } = "";
            private readonly HashSet<string> _tasks = new();
            private readonly object _gate = new();
            private StreamWriter _writer;
            private static string Key(BuildEventContext context) => context.NodeId + ":" + context.ProjectContextId + ":" + context.TargetId + ":" + context.TaskId;
            public void Initialize(IEventSource source)
            {
                _writer = new StreamWriter(Parameters, false, new UTF8Encoding(false));
                if (source is not IEventSource3 inputs) throw new InvalidOperationException("Task inputs are unavailable.");
                inputs.IncludeTaskInputs();
                source.AnyEventRaised += (_, message) =>
                {
                    lock (_gate)
                    {
                        if (message is TaskStartedEventArgs started && (started.TaskName == "Csc" || started.TaskName.EndsWith(".Csc", StringComparison.Ordinal)))
                            _tasks.Add(Key(started.BuildEventContext));
                        if (message.BuildEventContext == null || !_tasks.Contains(Key(message.BuildEventContext))) return;
                        if (message is TaskCommandLineEventArgs command)
                            Write(new { Kind = "CommandLine", Values = new[] { command.CommandLine } });
                        if (message is TaskParameterEventArgs parameter &&
                            (parameter.Kind == TaskParameterMessageKind.TaskOutput && parameter.ParameterName == "CommandLineArgs"
                            || parameter.Kind == TaskParameterMessageKind.TaskInput && (parameter.ParameterName == "OutputAssembly"
                                || parameter.ParameterName == "References")))
                        {
                            Write(new { Kind = parameter.ParameterName, Values = parameter.Items.Cast<object>().Select(item =>
                                item is ITaskItem task ? task.ItemSpec : (string)item).ToArray() });
                            Write(new { Kind = parameter.ParameterName + "Escaped", Values = parameter.Items.Cast<object>().Select(item =>
                                item is ITaskItem2 task ? task.EvaluatedIncludeEscaped : throw new InvalidOperationException("Native escaped item representation is unavailable.")).ToArray() });
                        }
                    }
                };
            }
            private void Write(object value) { _writer.WriteLine(JsonSerializer.Serialize(value)); _writer.Flush(); }
            public void Shutdown() => _writer.Dispose();
        }
        """);
}
