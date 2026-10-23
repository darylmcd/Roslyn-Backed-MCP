using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests.Helpers;

internal static class CompilerArgumentStageAssertions
{
    internal static void AssertReclaimed(string stagePath)
    {
        PhysicalDirectoryScope? stage = null;
        Exception? primary = null;
        try
        {
            stage = PhysicalDirectoryScope.OpenRouteIfPresent(stagePath);
            if (OperatingSystem.IsWindows())
                Assert.IsNull(stage, "Windows must remove the actual issued parser stage.");
            else if (stage is not null)
                Assert.IsFalse(stage.HasReclaimableContent(CancellationToken.None),
                    "POSIX may retain only a physical directory skeleton, with no files, links or other entries.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Parser stage assertion scope release failed.", primary, stage);
        }
    }
}
