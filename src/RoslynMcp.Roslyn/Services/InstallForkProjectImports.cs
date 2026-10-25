using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Installs a direct import before a copied project enters the native build engine.</summary>
public sealed class InstallForkProjectImports : ForkAdmissionTask
{
    [Required]
    public ITaskItem[] Projects { get; set; } = [];

    [Required]
    public string BaseDirectory { get; set; } = string.Empty;

    [Required]
    public string ForkRoot { get; set; } = string.Empty;

    [Required]
    public string GuardTargetsPath { get; set; } = string.Empty;

    private protected override void ExecuteAdmission(ForkAdmissionMaterial.Configuration configuration)
    {
        foreach (var project in Projects)
            ForkProjectImports.Install(Path.GetFullPath(project.ItemSpec, BaseDirectory), ForkRoot, GuardTargetsPath, CheckCancellation);
    }
}

internal static class ForkProjectImports
{
    internal const string Label = "RoslynMcpForkAdmission";

    internal static void Install(string projectPath, string forkRoot, string guardTargetsPath, Action checkCancellation)
    {
        checkCancellation();
        var fork = PhysicalPathResolver.Resolve(forkRoot);
        var project = PhysicalPathResolver.Resolve(projectPath);
        var guard = PhysicalPathResolver.Resolve(guardTargetsPath);
        if (!Contains(fork, project) || !Contains(fork, guard))
            throw new PublicInvalidOperationException(
                "The copied project graph references a project or admission material outside the current fork.",
                InvalidOperationErrors.Internal("A native fork project import is outside its current fork."));

        var bytes = ReadShared(project);
        using var input = new MemoryStream(bytes, writable: false);
        var document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        checkCancellation();
        var root = document.Root;
        if (root is null || root.Name.LocalName != "Project")
            throw InvalidOperationErrors.Internal("A copied native build entry is not an MSBuild project.");

        var existing = root.Elements(root.Name.Namespace + "Import")
            .Where(element => (string?)element.Attribute("Label") == Label &&
                FileSystemPath.Comparer.Equals((string?)element.Attribute("Project"), guard))
            .ToArray();
        if (existing.Length == 1 && existing[0].Attributes().Count() == 2)
            return;
        if (existing.Length != 0)
            throw InvalidOperationErrors.Internal("A copied native project contains a malformed owned admission import.");

        root.Add(new XElement(root.Name.Namespace + "Import", new XAttribute("Project", guard), new XAttribute("Label", Label)));
        Write(project, document, bytes, checkCancellation);
    }

    internal static byte[] ReadShared(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return bytes.ToArray();
    }

    internal static void Write(string project, XDocument document, byte[] original, Action checkCancellation)
    {
        var temporary = project + ".roslynmcp-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings
            {
                Encoding = SourceFileEncoding.FromBytes(original),
                OmitXmlDeclaration = document.Declaration is null,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
            }))
                document.Save(writer);
            checkCancellation();
            File.Move(temporary, project, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool Contains(string root, string path) =>
        FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path);
}
