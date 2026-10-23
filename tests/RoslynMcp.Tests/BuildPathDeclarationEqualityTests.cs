using System.Collections.Immutable;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class BuildPathDeclarationEqualityTests
{
    [TestMethod]
    public void EqualOperands_FromIndependentEvaluations_HaveEqualValueAndHashIdentity()
    {
        var first = Declaration();
        var second = Declaration();
        Assert.IsFalse(first.ExistsOperands.Equals(second.ExistsOperands),
            "The fixture must allocate independent nonempty operand arrays.");

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.IsTrue(new HashSet<BuildPathRoles.Declaration> { first }.Contains(second));
        Assert.HasCount(1, new[] { first, second }.Distinct().ToArray());
    }

    [TestMethod]
    [DataRow("FilePath")]
    [DataRow("Ordinal")]
    [DataRow("Name")]
    [DataRow("Attribute")]
    [DataRow("Expression")]
    [DataRow("Expanded")]
    [DataRow("OperandValue")]
    [DataRow("OperandOrder")]
    [DataRow("OperandSuffix")]
    [DataRow("OperandCase")]
    [DataRow("DefaultOperands")]
    public void DifferentDeclarationIntent_RemainsDistinct(string component)
    {
        var first = Declaration();
        var changed = component switch
        {
            "FilePath" => first with { FilePath = "Other.targets" },
            "Ordinal" => first with { Ordinal = 2 },
            "Name" => first with { Name = "Import" },
            "Attribute" => first with { Attribute = null },
            "Expression" => first with { Expression = "Exists('other')" },
            "Expanded" => first with { Expanded = "Exists('changed')" },
            "OperandValue" => first with { ExistsOperands = ["changed", "second/"] },
            "OperandOrder" => first with { ExistsOperands = ["second/", "first"] },
            "OperandSuffix" => first with { ExistsOperands = ["first", "second"] },
            "OperandCase" => first with { ExistsOperands = ["FIRST", "second/"] },
            "DefaultOperands" => first with { ExistsOperands = default },
            _ => throw new AssertFailedException("Unknown fixture component.")
        };

        Assert.AreNotEqual(first, changed);
        Assert.IsFalse(new HashSet<BuildPathRoles.Declaration> { first }.Contains(changed));
        Assert.HasCount(2, new[] { first, changed }.Distinct().ToArray());
    }

    [TestMethod]
    public void MissingOperandInventory_IsDistinctFromAnExplicitEmptyInventory()
    {
        var missing = Declaration() with { ExistsOperands = default };
        var empty = missing with { ExistsOperands = ImmutableArray<string>.Empty };
        Assert.AreNotEqual(missing, empty);
        Assert.IsFalse(new HashSet<BuildPathRoles.Declaration> { missing }.Contains(empty));
    }

    private static BuildPathRoles.Declaration Declaration() => new("Owned.targets", 1, "PropertyGroup", "Condition",
        "Exists('$(First)') And Exists('$(Second)')", "Exists('first') And Exists('second/')",
        ImmutableArray.CreateRange(new[] { "first", "second/" }));
}
