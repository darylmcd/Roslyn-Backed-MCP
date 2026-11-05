using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// <see cref="RestoreStalenessDetector"/> double with a fixed restore verdict that counts how often the
/// restore-required projection asked for it. The restore-race wait keeps its real behaviour.
/// </summary>
internal sealed class StubRestoreStalenessDetector(bool restoreRequired = false) : RestoreStalenessDetector
{
    private int _detectCalls;

    public int DetectCalls => Volatile.Read(ref _detectCalls);

    public override bool DetectRestoreRequired(
        ImmutableArray<ProjectStatusDto> projects,
        IReadOnlyDictionary<string, string> globalProperties,
        ILogger logger)
    {
        Interlocked.Increment(ref _detectCalls);
        return restoreRequired;
    }
}
