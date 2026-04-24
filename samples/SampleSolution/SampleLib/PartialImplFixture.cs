namespace SampleLib.PartialImplFixture;

/// <summary>
/// Fixture for find-implementations-source-gen-partial-dedup regression. A real source
/// generator (e.g. [LoggerMessage], [GeneratedRegex]) adds an extra partial declaration
/// with a .g.cs-suffixed file path. The companion file
/// <c>PartialImplFixture.Generated.g.cs</c> simulates that by adding a second partial
/// declaration of <see cref="SnapshotStore"/> — same type, distinct declaration site.
/// </summary>
public interface ISnapshotFixtureStore
{
    string Name { get; }
}

public partial class SnapshotStore : ISnapshotFixtureStore
{
    public string Name => "snapshot-store";
}

public sealed class OtherStore : ISnapshotFixtureStore
{
    public string Name => "other-store";
}
