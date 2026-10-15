using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Verifies ordered structural equality and stable read-only edit snapshots.
/// Constructor and init inputs cannot mutate an existing DTO or its hash-set membership.
/// Named construction, deconstruction and JSON retain the public record shape.
/// </summary>
[TestClass]
public sealed class FileEditsDtoTests
{
    private const string SamplePath = "/work/Sample.cs";

    private static TextEditDto Edit(int startLine, string newText) =>
        new(startLine, 1, startLine, 5, newText);

    [TestMethod]
    [DataRow("array")]
    [DataRow("list")]
    public void Snapshot_ConstructorKeepsCallerMutationOutOfValueAndHashSet(string kind)
    {
        IList<TextEditDto> owner = kind == "array" ? new[] { Edit(1, "original") } : new List<TextEditDto> { Edit(1, "original") };
        var dto = new FileEditsDto(FilePath: SamplePath, Edits: (IReadOnlyList<TextEditDto>)owner);
        var indexed = new HashSet<FileEditsDto> { dto };
        owner[0] = Edit(1, "changed");

        Assert.AreEqual(Edit(1, "original"), dto.Edits[0]);
        Assert.IsTrue(indexed.Contains(dto), "Caller mutation must not invalidate a DTO already used as a value key.");
    }

    [TestMethod]
    [DataRow("array")]
    [DataRow("list")]
    public void Snapshot_WithAssignmentCopiesCallerCollection(string kind)
    {
        IList<TextEditDto> owner = kind == "array" ? new[] { Edit(2, "replacement") } : new List<TextEditDto> { Edit(2, "replacement") };
        var original = new FileEditsDto(SamplePath, [Edit(1, "original")]);
        var updated = original with { Edits = (IReadOnlyList<TextEditDto>)owner };
        owner[0] = Edit(2, "changed");

        Assert.AreEqual(Edit(2, "replacement"), updated.Edits[0]);
        Assert.AreEqual(Edit(1, "original"), original.Edits[0]);
    }

    [TestMethod]
    public void Snapshot_PublicListViewCannotMutateStoredEdits()
    {
        var dto = new FileEditsDto(SamplePath, new[] { Edit(1, "original") });
        if (dto.Edits is IList<TextEditDto> list)
            Assert.ThrowsExactly<NotSupportedException>(() => list[0] = Edit(1, "changed"));
        Assert.AreEqual(Edit(1, "original"), dto.Edits[0]);
    }

    [TestMethod]
    public void Snapshot_LegacySynchronizationViewCannotMutateStoredEdits()
    {
        var dto = new FileEditsDto(SamplePath, new[] { Edit(1, "original") });
        if (dto.Edits is System.Collections.ICollection legacy)
        {
            object synchronizationRoot;
            try { synchronizationRoot = legacy.SyncRoot; }
            catch (NotSupportedException) { return; }
            if (synchronizationRoot is IList<TextEditDto> mutable)
            {
                try { mutable[0] = Edit(1, "changed"); }
                catch (NotSupportedException) { }
            }
        }
        Assert.AreEqual(Edit(1, "original"), dto.Edits[0]);
    }

    [TestMethod]
    public void Snapshot_RecordDeconstructionAndJsonRoundTripKeepPublicShape()
    {
        var dto = new FileEditsDto(FilePath: SamplePath, Edits: [Edit(1, "original")]);
        var (path, edits) = dto;
        Assert.AreEqual(SamplePath, path);
        CollectionAssert.AreEqual(dto.Edits.ToArray(), edits.ToArray());
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var json = System.Text.Json.JsonSerializer.Serialize(dto, options);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        CollectionAssert.AreEquivalent(new[] { "filePath", "edits" }, document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<FileEditsDto>(json, options);
        Assert.AreEqual(dto, roundTrip);
        Assert.AreEqual(dto.GetHashCode(), roundTrip!.GetHashCode());
    }

    [TestMethod]
    public void Equals_SeparatelyAllocatedEqualArrays_AreEqualAndHashEqual()
    {
        var left = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });
        var right = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });

        Assert.AreNotSame(left.Edits, right.Edits, "the two DTOs must hold distinct backing sequences for this to prove anything");
        Assert.AreEqual(left, right);
        Assert.IsTrue(left == right, "record-generated == must route through the structural Equals override");
        Assert.IsFalse(left != right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode(), "Equals-true instances must hash equal");
    }

    [TestMethod]
    public void Equals_ListAndArrayWithSameContent_AreEqualAndHashEqual()
    {
        var fromArray = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });
        var fromList = new FileEditsDto(SamplePath, new List<TextEditDto> { Edit(1, "a"), Edit(2, "b") });

        Assert.AreEqual(fromArray, fromList, "equality must be structural, not dependent on the concrete collection type");
        Assert.AreEqual(fromArray.GetHashCode(), fromList.GetHashCode());
    }

    [TestMethod]
    public void Equals_CollectionExpressionCallsite_StillCompilesAndBehavesIdentically()
    {
        // Both the `new[] { … }` and collection-expression construction forms used across the
        // existing edit-regression suites must keep working after the array -> IReadOnlyList swap.
        var fromCollectionExpression = new FileEditsDto(SamplePath, [Edit(1, "a")]);
        var fromArrayLiteral = new FileEditsDto(SamplePath, new[] { Edit(1, "a") });

        Assert.AreEqual(1, fromCollectionExpression.Edits.Count);
        Assert.AreEqual(fromArrayLiteral, fromCollectionExpression);
    }

    [TestMethod]
    public void Equals_DifferentFilePath_AreNotEqual()
    {
        var left = new FileEditsDto(SamplePath, new[] { Edit(1, "a") });
        var right = new FileEditsDto("/work/Other.cs", new[] { Edit(1, "a") });

        Assert.AreNotEqual(left, right, "FilePath must still participate in equality");
    }

    [TestMethod]
    public void Equals_SameLengthDifferentContent_AreNotEqual()
    {
        var left = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });
        var right = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "DIFFERENT") });

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void Equals_DifferentLengthSequences_AreNotEqual()
    {
        var left = new FileEditsDto(SamplePath, new[] { Edit(1, "a") });
        var right = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });

        Assert.AreNotEqual(left, right);
        Assert.AreNotEqual(right, left, "inequality must be symmetric");
    }

    [TestMethod]
    public void Equality_IsOrderSensitive_EqualSequencesHaveEqualHashes()
    {
        var forward = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });
        var reversed = new FileEditsDto(SamplePath, new[] { Edit(2, "b"), Edit(1, "a") });

        Assert.AreNotEqual(forward, reversed, "element order is part of the value");
        var equalForward = new FileEditsDto(SamplePath, new List<TextEditDto> { Edit(1, "a"), Edit(2, "b") });
        Assert.AreEqual(forward, equalForward);
        Assert.AreEqual(
            forward.GetHashCode(),
            equalForward.GetHashCode(),
            "equal sequences must hash equally; unequal values may collide");
    }

    [TestMethod]
    public void Equals_SelfAndNull_BehaveAsRecordEqualityRequires()
    {
        var dto = new FileEditsDto(SamplePath, new[] { Edit(1, "a") });

        Assert.AreEqual(dto, dto);
        Assert.IsFalse(dto.Equals(null));
        Assert.IsFalse(dto.Equals((object?)null));
        Assert.IsFalse(dto.Equals("not a FileEditsDto"));
    }
}
