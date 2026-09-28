namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// <c>host-recycle-opacity</c>: consume-once slot for the previous-host-process snapshot
/// captured by <see cref="HostProcessMetadataStore.LoadPrevious"/> at startup. One instance is
/// owned by each <see cref="Runtime.ServerProcessMetadata"/> (exposed as
/// <see cref="Runtime.ServerProcessMetadata.PreviousProcessSnapshot"/>), so <c>ServerTools</c>
/// reaches it through the DI-injected metadata rather than through process-wide static state.
/// <para>
/// <strong>Consume-once semantics:</strong> <see cref="Consume"/> returns the snapshot on the
/// first call after <see cref="Publish"/> and <see langword="null"/> on every subsequent call.
/// This is enforced under a lock so two concurrent <c>server_info</c> probes during a startup
/// race don't both see the previous-* fields. The contract from the backlog row is "first probe
/// after restart carries previous-*; second probe does not" — the PROVIDER, not the consumer,
/// owns that invariant.
/// </para>
/// <para>
/// State is per instance: two instances never share a snapshot or a consume-once latch. Tests
/// construct their own <see cref="Runtime.ServerProcessMetadata"/> and publish into it, so a
/// concurrently running test class cannot drain another class's snapshot.
/// </para>
/// </summary>
internal sealed class HostProcessMetadataSnapshotProvider
{
    private readonly object _lock = new();
    private HostProcessMetadataSnapshot? _snapshot;
    private bool _consumed;

    /// <summary>
    /// Publishes a snapshot for consumption by the next <see cref="Consume"/> call. Pass
    /// <see langword="null"/> to declare "no previous-process record was found" — every
    /// <see cref="Consume"/> call then returns <see langword="null"/>.
    /// <para>
    /// Calling <see cref="Publish"/> a second time after <see cref="Consume"/> has already
    /// drained the first snapshot resets the consume-once latch — useful for tests that
    /// simulate multiple restart cycles on one instance. Production code calls
    /// <see cref="Publish"/> exactly once at host startup.
    /// </para>
    /// </summary>
    public void Publish(HostProcessMetadataSnapshot? snapshot)
    {
        lock (_lock)
        {
            _snapshot = snapshot;
            _consumed = false;
        }
    }

    /// <summary>
    /// Returns the published snapshot exactly once. The first call after <see cref="Publish"/>
    /// returns the snapshot if one was published; every subsequent call returns
    /// <see langword="null"/>. Thread-safe — concurrent first-callers race on the lock and
    /// only one observes the non-null snapshot.
    /// </summary>
    public HostProcessMetadataSnapshot? Consume()
    {
        lock (_lock)
        {
            if (_consumed)
            {
                return null;
            }

            _consumed = true;
            return _snapshot;
        }
    }
}
