using System.Runtime.CompilerServices;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeMembershipEncodingTests
{
    [TestMethod]
    [DataRow("metadata-order", true)]
    [DataRow("metadata-name-case", true)]
    [DataRow("item-order", false)]
    [DataRow("duplicate", false)]
    [DataRow("include", false)]
    [DataRow("item-type", false)]
    [DataRow("metadata-value", false)]
    [DataRow("surrogate", false)]
    public void CombinedEvaluation_PreservesExactOrderedItemIdentity(string change, bool equal)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        AssertEvaluation(change, equal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertEvaluation(string change, bool equal)
    {
        Microsoft.Build.Evaluation.ProjectCollection? collection = null;
        CancellationTokenSource? cancelled = null;
        Exception? primary = null;
        try
        {
            collection = new Microsoft.Build.Evaluation.ProjectCollection();
            Microsoft.Build.Execution.ProjectInstance Create(bool changed)
            {
                var root = Microsoft.Build.Construction.ProjectRootElement.Create(collection);
                root.FullPath = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"), "Identity.csproj");
                var instance = new Microsoft.Build.Execution.ProjectInstance(root, new Dictionary<string, string>(), null, collection);
                var metadata = new List<KeyValuePair<string, string>>
                {
                    new("Alpha", change == "surrogate" ? new string((char)0xd800, 1) : "value"), new("Beta", "two")
                };
                if (changed)
                {
                    if (change == "metadata-order") metadata.Reverse();
                    if (change == "metadata-name-case") metadata[0] = new("aLPHA", "value");
                    if (change == "metadata-value") metadata[0] = new("Alpha", "changed");
                    if (change == "surrogate") metadata[0] = new("Alpha", new string((char)0xfffd, 1));
                }
                var names = changed && change == "item-order" ? new[] { "B.cs", "A.cs" } : new[] { "A.cs", "B.cs" };
                foreach (var name in names)
                    instance.AddItem(changed && change == "item-type" ? "None" : "Compile",
                        changed && change == "include" ? "Changed" + name : name, metadata);
                if (changed && change == "duplicate") instance.AddItem("Compile", "A.cs", metadata);
                return instance;
            }
            var before = NativeBuildMembership.CaptureEvaluation(Create(false), CancellationToken.None);
            var after = NativeBuildMembership.CaptureEvaluation(Create(true), CancellationToken.None);
            if (equal) Assert.AreEqual(before.Items, after.Items);
            else Assert.AreNotEqual(before.Items, after.Items);
            Assert.AreEqual(2, before.Files.Length);
            Assert.AreNotEqual(before.Files[0].Identity, before.Files[1].Identity);
            cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => NativeBuildMembership.CaptureEvaluation(Create(false), cancelled.Token));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native fixture cleanup failed.", primary, cancelled, collection); }
    }

    [TestMethod]
    public void CaptureScopedSerializer_PreservesExactStringsAndChecksCurrentCancellation()
    {
        CancellationTokenSource? cancellation = null;
        Exception? primary = null;
        try
        {
            cancellation = new CancellationTokenSource();
            var serializer = new NativeStructuredIdentity.Session(cancellation.Token);
            var surrogate = new string((char)0xd800, 1);
            var values = new Dictionary<string, string> { [surrogate] = "value", ["second"] = surrogate };
            var reordered = new Dictionary<string, string> { ["second"] = surrogate, [surrogate] = "value" };
            var expected = NativeStructuredIdentity.Serialize(values, CancellationToken.None);
            Assert.AreEqual(expected, serializer.Serialize(values));
            Assert.AreEqual(expected, serializer.Serialize(reordered));
            Assert.AreNotEqual(expected, serializer.Serialize(new Dictionary<string, string> { [new string((char)0xfffd, 1)] = "value", ["second"] = surrogate }));
            cancellation.Cancel();
            Assert.ThrowsExactly<OperationCanceledException>(() => serializer.Serialize(values));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native serializer cancellation cleanup failed.", primary, cancellation); }
    }
}
