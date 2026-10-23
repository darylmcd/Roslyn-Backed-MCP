using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>The installed native Csc task emits the actual reference/alias argument grammar.</summary>
internal static class NativeCscReferenceArguments
{
    internal static ImmutableArray<string> Generate(string path, string aliases, bool embed)
    {
        var (task, taskType, itemType) = CreateTask();
        var item = Activator.CreateInstance(itemType, Microsoft.Build.Evaluation.ProjectCollection.Escape(path))!;
        var metadata = itemType.GetMethod("SetMetadata", [typeof(string), typeof(string)])!;
        metadata.Invoke(item, ["Aliases", aliases]);
        metadata.Invoke(item, ["EmbedInteropTypes", embed.ToString()]);
        var references = taskType.GetProperty("References")!;
        var items = Array.CreateInstance(references.PropertyType.GetElementType()!, 1);
        items.SetValue(item, 0);
        references.SetValue(task, items);
        var text = (string)taskType.GetMethod("GenerateResponseFileCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(task, null)!;
        return CommandLineParser.SplitCommandLineIntoArguments(text, removeHashComments: false).ToImmutableArray();
    }

    internal sealed record OutputTransport(ImmutableArray<string> Arguments, ImmutableArray<string> UnadjustedArguments,
        ImmutableArray<string> ItemSpecs, ImmutableArray<string> EscapedIncludes, string ToolPath,
        ImmutableArray<string> HostArguments, string LoggedCommand);

    internal static OutputTransport GenerateOutputTransport(string output, string source, string features, string reference)
    {
        var (task, type, itemType) = CreateTask();
        type.GetProperty("OutputAssembly")!.SetValue(task,
            Activator.CreateInstance(itemType, Microsoft.Build.Evaluation.ProjectCollection.Escape(output)));
        type.GetProperty("TargetType")!.SetValue(task, "library");
        type.GetProperty("Features")!.SetValue(task, features);
        var sources = type.GetProperty("Sources")!;
        var values = Array.CreateInstance(sources.PropertyType.GetElementType()!, 1);
        values.SetValue(Activator.CreateInstance(itemType, Microsoft.Build.Evaluation.ProjectCollection.Escape(source)), 0);
        sources.SetValue(task, values);
        var references = type.GetProperty("References")!;
        var referenceItems = Array.CreateInstance(references.PropertyType.GetElementType()!, 1);
        referenceItems.SetValue(Activator.CreateInstance(itemType, Microsoft.Build.Evaluation.ProjectCollection.Escape(reference)), 0);
        references.SetValue(task, referenceItems);
        var response = (string)type.GetMethod("GenerateResponseFileCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(task, null)!;
        var unadjustedArguments = ((IEnumerable<string>)type.GetMethod("GenerateCommandLineArgsList", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(task, [response])!).ToImmutableArray();
        string Invoke(string name, params object[] parameters)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method is not null) return (string)method.Invoke(task, parameters)!;
            }
            throw new AssertFailedException("The selected installed compiler task lacks the required native command oracle: " + name);
        }
        var toolArguments = Invoke("AdjustCommandsForOperatingSystem", Invoke("GenerateToolArguments"));
        var command = Invoke("GenerateCommandLineCommands");
        var toolPath = Invoke("GeneratePathToTool");
        var adjustedCommand = Invoke("AdjustCommandsForOperatingSystem", command);
        var adjustedResponse = Invoke("AdjustCommandsForOperatingSystem", response);
        var directArguments = CommandLineParser.SplitCommandLineIntoArguments(toolArguments, removeHashComments: true).ToImmutableArray();
        var commandArguments = CommandLineParser.SplitCommandLineIntoArguments(adjustedCommand, removeHashComments: true).ToImmutableArray();
        Assert.IsTrue(commandArguments.Length >= directArguments.Length);
        CollectionAssert.AreEqual(directArguments.ToArray(), commandArguments.Skip(commandArguments.Length - directArguments.Length).ToArray(),
            "The installed command prefix must end in its independently generated direct compiler arguments.");
        var hostArguments = commandArguments.Take(commandArguments.Length - directArguments.Length).ToImmutableArray();
        var arguments = directArguments.AddRange(CommandLineParser.SplitCommandLineIntoArguments(adjustedResponse, removeHashComments: true));
        var transported = (Array)type.GetMethod("GenerateCommandLineArgsTaskItems", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(string)], null)!.Invoke(task, [adjustedResponse])!;
        var framework = type.GetProperty("References")!.PropertyType.GetElementType()!.Assembly;
        var item2 = framework.GetType("Microsoft.Build.Framework.ITaskItem2", throwOnError: true)!;
        return new(arguments, unadjustedArguments,
            transported.Cast<object>().Select(item => (string)itemType.GetProperty("ItemSpec")!.GetValue(item)!).ToImmutableArray(),
            transported.Cast<object>().Select(item => (string)item2.GetProperty("EvaluatedIncludeEscaped")!.GetValue(item)!).ToImmutableArray(),
            toolPath, hostArguments, toolPath + (command.Length == 0 ? "" : " " + adjustedCommand) + " " + adjustedResponse);
    }

    private static (object Task, Type Type, Type ItemType) CreateTask()
    {
        var sdk = BuildInputProvenance.RegisteredSdkDirectory();
        var utilities = Assembly.LoadFrom(Path.Combine(sdk, "Microsoft.Build.Utilities.Core.dll"));
        _ = Assembly.LoadFrom(Path.Combine(sdk, "Microsoft.Build.Tasks.Core.dll"));
        var taskAssembly = Assembly.LoadFrom(Path.Combine(sdk, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"));
        var itemType = utilities.GetType("Microsoft.Build.Utilities.TaskItem", throwOnError: true)!;
        var taskType = taskAssembly.GetType("Microsoft.CodeAnalysis.BuildTasks.Csc", throwOnError: true)!;
        Assert.AreEqual(Path.GetFullPath(Path.Combine(sdk, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll")),
            Path.GetFullPath(taskType.Assembly.Location),
            OperatingSystem.IsWindows(),
            "The native Csc oracle must execute the selected installed SDK task assembly.");
        var task = Activator.CreateInstance(taskType)!;
        taskType.GetProperty("NoStandardLib")!.SetValue(task, true);
        return (task, taskType, itemType);
    }

}
