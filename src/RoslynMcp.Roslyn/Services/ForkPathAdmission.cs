using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Shared physical source/fork authority with consumer-specific lexical normalization.</summary>
internal sealed class ForkPathAdmission
{
    private readonly string _source;
    private readonly string _fork;
    private readonly string _project;
    private readonly string _baseDirectory;
    private readonly Action _checkCancellation;

    internal ForkPathAdmission(string source, string fork, string project, Action checkCancellation)
    {
        _source = PhysicalPathResolver.Resolve(source);
        _fork = PhysicalPathResolver.Resolve(fork);
        _project = project;
        _baseDirectory = Path.GetDirectoryName(project)
            ?? throw InvalidOperationErrors.Internal("An evaluated fork project has no directory.");
        _checkCancellation = checkCancellation;
        if (!Contains(_fork, PhysicalPathResolver.Resolve(project)))
            throw new PublicInvalidOperationException(
                "The copied project graph still references an original source workspace project outside the fork. Include that project in the copied workspace before retrying.",
                InvalidOperationErrors.Internal("An evaluated native project is outside the current fork."));
    }

    internal void Check(string value, string role, bool normalizeNativeOperation)
    {
        _checkCancellation();
        if (string.IsNullOrWhiteSpace(value)) return;
        var expression = normalizeNativeOperation
            ? Path.GetFullPath(value, _baseDirectory)
            : Path.IsPathRooted(value) ? value : Path.Join(_baseDirectory, value);
        var candidate = PhysicalPathResolver.Resolve(expression);
        if (Contains(_fork, candidate)) return;
        if (Contains(_source, candidate))
            throw new PublicInvalidOperationException(
                "The copied project graph refers to files or outputs in the source workspace. Make those paths fork-relative or use an independent external location before retrying.",
                InvalidOperationErrors.Internal($"Evaluated fork project '{_project}' role '{role}' reaches source path '{candidate}'."));
    }

    internal static bool Contains(string root, string path) =>
        FileSystemPath.Comparer.Equals(root, path) || FileSystemPath.IsStrictDescendant(root, path);
}
