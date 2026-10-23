using System.Text;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Identifies native project contexts by physical project and evaluated output paths.</summary>
internal static class PhysicalProjectIdentity
{
    internal static string Key(Project project, Func<string, string> mapPath)
    {
        var path = project.FilePath ?? throw new PublicInvalidOperationException("A project has no physical identity.");
        return Key(path, project.OutputFilePath, mapPath);
    }

    internal static string Key(string path, string? outputPath, Func<string, string> mapPath)
    {
        string Normalize(string value)
        {
            var normalized = Path.GetFullPath(mapPath(value));
            return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            NativeIdentityEncoding.WriteString(writer, Normalize(path), CancellationToken.None);
            NativeIdentityEncoding.WriteString(writer, outputPath is { Length: > 0 } output ? Normalize(output) : string.Empty, CancellationToken.None);
        }
        // Consumer maps intentionally use the host path comparer. Uppercase
        // hex keeps exact framed bytes injective even under case folding.
        return Convert.ToHexString(bytes.ToArray());
    }

    internal static Project? Find(Solution solution, Project source)
    {
        var candidates = solution.Projects.Where(project => FileSystemPath.Comparer.Equals(project.FilePath, source.FilePath)).ToArray();
        if (source.OutputFilePath is { Length: > 0 } output)
            candidates = candidates.Where(project => FileSystemPath.Comparer.Equals(project.OutputFilePath, output)).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1) throw new PublicInvalidOperationException(
            "The project cannot be identified unambiguously across evaluated output contexts.");
        return candidates[0];
    }
}
