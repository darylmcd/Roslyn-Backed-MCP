namespace RoslynMcp.Tests.Helpers;

/// <summary>Captures native RAR task items directly in the installed Csc task before any string round trip.</summary>
internal static class NativeRarCscArgumentOracle
{
    internal static string Create(string root)
    {
        return NativeSdkOracleAssembly.Create(root, "NativeRarCscOracle", """
                using System;
                using System.IO;
                using System.Reflection;
                using System.Text;
                using Microsoft.Build.Framework;
                public sealed class NativeRarCscOracle : Microsoft.Build.Utilities.Task
                {
                    [Required] public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();
                    [Required] public string CscTaskAssembly { get; set; } = "";
                    [Required] public string ReceiptPath { get; set; } = "";
                    public override bool Execute()
                    {
                        var assembly = Assembly.LoadFrom(CscTaskAssembly);
                        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        if (!string.Equals(Path.GetFullPath(CscTaskAssembly), Path.GetFullPath(assembly.Location), comparison))
                            throw new InvalidOperationException("The selected installed Csc task was not loaded.");
                        var type = assembly.GetType("Microsoft.CodeAnalysis.BuildTasks.Csc", true);
                        var task = Activator.CreateInstance(type);
                        type.GetProperty("NoStandardLib").SetValue(task, true);
                        var references = type.GetProperty("References");
                        if (references.PropertyType.GetElementType() != typeof(ITaskItem))
                            throw new InvalidOperationException("The native RAR and Csc task item identities differ.");
                        references.SetValue(task, References);
                        var text = (string)type.GetMethod("GenerateResponseFileCommands", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(task, null);
                        File.WriteAllText(ReceiptPath, text, new UTF8Encoding(false));
                        return true;
                    }
                }
                """);
    }
}
