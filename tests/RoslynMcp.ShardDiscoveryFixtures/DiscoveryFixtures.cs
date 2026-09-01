using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RoslynMcp.ShardDiscoveryFixtures;

[TestClass]
public abstract class InheritedTestBase
{
    [TestMethod]
    public void InheritedTestMethod()
    {
    }
}

[TestClass]
public sealed class InheritedTestClass : InheritedTestBase
{
}

public sealed class CustomTestClassAttribute : TestClassAttribute
{
}

[CustomTestClass]
public sealed class CustomAttributedTestClass
{
    [TestMethod]
    public void CustomAttributedTestMethod()
    {
    }
}

[TestClass]
public sealed class ParameterizedTestClass
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ParameterizedTestMethod(int value)
        => Assert.IsTrue(value > 0);
}
