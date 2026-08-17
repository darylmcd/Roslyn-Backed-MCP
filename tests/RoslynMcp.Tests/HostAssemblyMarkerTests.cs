using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Diagnostics;
using RoslynMcp.Host.Stdio.Tools;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class HostAssemblyMarkerTests
{
    public static IEnumerable<object[]> HostTypes
    {
        get
        {
            yield return ["legacy logging provider", typeof(McpLoggingProvider)];
            yield return ["startup diagnostics", typeof(StartupDiagnostics)];
            yield return ["tool surface", typeof(SuppressionTools)];
        }
    }

    [TestMethod]
    [DynamicData(nameof(HostTypes))]
    public void Marker_ResolvesSameAssemblyAsEveryAnchoredHostConsumer(string consumer, Type hostType)
    {
        Assert.AreSame(
            typeof(HostAssemblyMarker).Assembly,
            hostType.Assembly,
            $"{consumer} must resolve through the stable Host assembly identity.");
    }
}
