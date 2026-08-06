using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression for `core-dto-fileeditsdto-array-to-readonlylist` (P4): `FileEditsDto.Edits` was
/// declared as `TextEditDto[]`, so the compiler-synthesized record `Equals`/`GetHashCode` routed
/// the property through `EqualityComparer&lt;TextEditDto[]&gt;.Default` — `Array` never overrides
/// `object.Equals`, so two instances holding distinct-but-content-equal edit sequences always
/// compared unequal, defeating the point of the record. The property is now
/// `IReadOnlyList&lt;TextEditDto&gt;` (closing the aliased-array mutation surface) with explicit
/// structural `Equals`/`GetHashCode` overrides (restoring value equality).
/// </summary>
[TestClass]
public sealed class FileEditsDtoTests
{
    private const string SamplePath = "/work/Sample.cs";

    private static TextEditDto Edit(int startLine, string newText) =>
        new(startLine, 1, startLine, 5, newText);

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
    public void GetHashCode_IsOrderSensitive_NotCountBased()
    {
        var forward = new FileEditsDto(SamplePath, new[] { Edit(1, "a"), Edit(2, "b") });
        var reversed = new FileEditsDto(SamplePath, new[] { Edit(2, "b"), Edit(1, "a") });

        Assert.AreNotEqual(forward, reversed, "element order is part of the value");
        Assert.AreNotEqual(
            forward.GetHashCode(),
            reversed.GetHashCode(),
            "hash must fold each element in order, not merely the element count");
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
