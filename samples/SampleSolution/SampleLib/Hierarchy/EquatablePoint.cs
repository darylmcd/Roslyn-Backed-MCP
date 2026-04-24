using System;

namespace SampleLib.Hierarchy;

/// <summary>
/// Fixture for <c>find-base-members-vs-member-hierarchy-metadata-drift</c>. Implementing
/// <see cref="IEquatable{T}"/> creates a metadata-boundary base member (the interface sits
/// in corlib). Before the fix, <c>find_base_members</c>/<c>find_overrides</c> returned 0
/// while <c>member_hierarchy</c> resolved the base — all three now agree.
/// </summary>
public readonly struct EquatablePoint : IEquatable<EquatablePoint>
{
    public EquatablePoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }

    public bool Equals(EquatablePoint other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is EquatablePoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);
}
