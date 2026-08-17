using RoslynMcp.Host.Stdio.Diagnostics;

namespace RoslynMcp.Tests.TestInfrastructure;

/// <summary>Enabled in-memory observability sink for secret-boundary regression tests.</summary>
internal sealed class CapturingServerObservabilitySink : IServerObservabilitySink
{
    public List<ServerObservabilityEvent> Events { get; } = [];

    public bool IsEnabled => true;

    public void Write(ServerObservabilityEvent diagnosticEvent) => Events.Add(diagnosticEvent);
}
