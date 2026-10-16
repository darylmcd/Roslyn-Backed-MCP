using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Compile check that remains pending until the caller cancels its token.</summary>
internal sealed class CancellationOnlyCompileCheckService : ICompileCheckService
{
    public async Task<CompileCheckDto> CheckAsync(
        string workspaceId,
        CompileCheckOptions options,
        CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("unreachable");
    }
}
