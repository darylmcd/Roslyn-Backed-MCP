using Microsoft.VisualStudio.TestTools.UnitTesting;
using SampleLib;

namespace SampleLib.Tests;

[TestClass]
public sealed class OpaqueConsumerTests
{
    [TestMethod]
    public void Direct_consumer_uses_value()
    {
        var target = new WidgetTarget();

        Assert.AreEqual("widget", target.Value());
    }
}
