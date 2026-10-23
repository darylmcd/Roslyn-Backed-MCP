using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeConditionCacheWarmupTests
{
    [TestMethod]
    public Task WarmNativeCache_BeforeFirstCondition_PreservesCanaryEvidence() =>
        NativeConditionProcessTest.RunAsync(typeof(NativeConditionCacheWarmupTests),
            nameof(WarmNativeCache_BeforeFirstCondition_PreservesCanaryEvidence),
            new Dictionary<string, string?>(), static () =>
            {
                RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
                VerifyWarmCacheBeforeFirstCondition();
            });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyWarmCacheBeforeFirstCondition()
    {
        var observer = new NativeConditionEvidence();
        Exception? primary = null;
        try
        {
            var owner = observer.Collection;
            owner.RegisterLogger(observer);
            var root = ProjectRootElement.Create(owner);
            root.FullPath = Path.Combine(TestTempRoot.Current, nameof(NativeConditionCacheWarmupTests),
                Guid.NewGuid().ToString("N"), "retained.proj");
            Assert.AreEqual(0, root.AllChildren.Count(),
                "The retained cache target must not evaluate a condition before warming the native cache.");
            var cacheProperty = typeof(ProjectCollection).GetProperty("ProjectRootElementCache",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cache = cacheProperty.GetValue(owner)!;
            var method = cache.GetType().GetMethod("TryGet", BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(string)], modifiers: null)!;
            Assert.IsNotNull(method, "The supported native cache must expose its exact one-string lookup.");
            var lookup = method.CreateDelegate<Func<string, ProjectRootElement?>>(cache);
            var elapsed = Stopwatch.StartNew();
            var calls = 0L;
            do
            {
                Assert.AreSame(root, lookup(root.FullPath),
                    "Every warmup lookup must return this exact already-retained native root.");
                calls++;
            } while (calls < 256 || elapsed.Elapsed < TimeSpan.FromSeconds(15));
            Assert.IsTrue(calls >= 256);
            Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromSeconds(15));

            // This is deliberately the first condition evaluation in the child:
            // native cache lookup is hot while the Exists implementation is cold.
            observer.VerifyProtocol(owner);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native cache warmup observer cleanup failed.", primary, observer);
        }
    }
}
