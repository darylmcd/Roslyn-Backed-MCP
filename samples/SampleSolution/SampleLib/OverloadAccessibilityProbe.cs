namespace SampleLib;

/// <summary>
/// Holds a private/protected overload pair on a base type so integration tests can assert
/// that `find_overloads(includeInherited: true)` excludes inaccessible base members instead of
/// advertising overloads the caller could never actually invoke on the derived type.
/// </summary>
public class OverloadAccessibilityProbeBase
{
    private void Probe()
    {
    }

    protected void Probe(int value)
    {
    }
}

public sealed class OverloadAccessibilityProbeDerived : OverloadAccessibilityProbeBase
{
}
