namespace RoslynMcp.Host.Stdio.Diagnostics;

/// <summary>
/// <c>host-recycle-opacity</c>: process-wide publisher for the previous-host-process snapshot
/// captured by <see cref="HostProcessMetadataStore.LoadPrevious"/> at startup. Mirrors the
/// <see cref="SurfaceRegistrationSnapshot"/> pattern so static <c>ServerTools</c> methods can
/// reach the snapshot without taking a constructor dependency.
/// <para>
/// <strong>Consume-once semantics:</strong> <see cref="Consume"/> returns the snapshot on the
/// first call after <see cref="Publish"/> and <see langword="null"/> on every subsequent call.
/// This is enforced atomically (volatile + lock) so two concurrent <c>server_info</c> probes
/// during a startup race don't both see the previous-* fields. The contract from the backlog
/// row is "first probe after restart carries previous-*; second probe does not" — the
/// PROVIDER, not the consumer, owns that invariant.
/// </para>
/// <para>
/// Test paths that construct <see cref="Tools.ServerTools"/> without booting the host either
/// leave the provider unpublished (getting clean cold-start behavior) or stage a snapshot via
/// <see cref="Publish"/> in a try/finally that calls <see cref="Reset"/> in the finally block.
/// </para>
/// </summary>
public static class HostProcessMetadataSnapshotProvider
{
    private static readonly object s_lock = new();
    private static volatile HostProcessMetadataSnapshot? s_snapshot;
    private static volatile bool s_consumed;

    /// <summary>
    /// Publishes a snapshot for consumption by the next <see cref="Consume"/> call. Pass
    /// <see langword="null"/> to declare "no previous-process record was found" — the provider
    /// then short-circuits all future <see cref="Consume"/> calls to <see langword="null"/>
    /// without waiting for the consume-once flag to flip.
    /// <para>
    /// Calling <see cref="Publish"/> a second time after <see cref="Consume"/> has already
    /// drained the first snapshot resets the consume-once latch — useful for tests that
    /// simulate multiple restart cycles in a single test run. Production code calls
    /// <see cref="Publish"/> exactly once at host startup.
    /// </para>
    /// </summary>
    public static void Publish(HostProcessMetadataSnapshot? snapshot)
    {
        lock (s_lock)
        {
            s_snapshot = snapshot;
            s_consumed = false;
        }
    }

    /// <summary>
    /// Returns the published snapshot exactly once. The first call after <see cref="Publish"/>
    /// returns the snapshot if one was published; every subsequent call returns
    /// <see langword="null"/>. Thread-safe — concurrent first-callers race on the lock and
    /// only one observes the non-null snapshot.
    /// </summary>
    public static HostProcessMetadataSnapshot? Consume()
    {
        lock (s_lock)
        {
            if (s_consumed)
            {
                return null;
            }

            s_consumed = true;
            return s_snapshot;
        }
    }

    /// <summary>
    /// Test hook — clears the published snapshot and resets the consume-once latch back to
    /// "ready to consume". Production code should never call this.
    /// </summary>
    public static void Reset()
    {
        lock (s_lock)
        {
            s_snapshot = null;
            s_consumed = false;
        }
    }
}
