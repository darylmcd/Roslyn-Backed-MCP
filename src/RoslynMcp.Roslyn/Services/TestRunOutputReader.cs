using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Reads only the held invocation namespace, before outward value redaction.</summary>
internal static class TestRunOutputReader
{
    internal static async Task<TestRunResultDto> ParseAsync(PhysicalDirectoryScope.OwnedRoot results,
        CommandExecutionDto execution, CancellationToken ct)
    {
        results.AssertCurrentPath(ct);
        var documents = new List<XDocument>();
        foreach (var relative in results.EnumerateRelativeFiles(ct).Order(FileSystemPath.Comparer))
        {
            ct.ThrowIfCancellationRequested();
            if (!relative.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)) continue;
            var bytes = await results.ReadFileIfPresentAsync(relative, ct).ConfigureAwait(false)
                ?? throw new IOException("An invocation report disappeared during collection.");
            using var stream = new MemoryStream(bytes, writable: false);
            documents.Add(await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false));
        }
        results.AssertCurrentPath(ct);
        // Process output is diagnostic text, never authority for another report path.
        return DotnetOutputParser.ParseTestRun(execution, documents);
    }
}
