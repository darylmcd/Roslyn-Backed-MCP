using RoslynMcp.Roslyn.Helpers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeConditionCacheObservationTests
{
    [TestMethod]
    public void RepeatedOwnedLifetimes_KeepMissingAndCachedCanaryEvidence()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        VerifyRepeatedOwners();
    }

    [TestMethod]
    public async Task SustainedOwnedLifetimes_KeepCanaryEvidenceAcrossRuntimeTierTransitions()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        await VerifySustainedOwnersAsync();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task VerifySustainedOwnersAsync()
    {
        // The observed late canary failure occurred after eight seconds of native
        // work. Exercise continuously beyond that window, including continuations;
        // a fast fixed iteration count alone may finish before tiered JIT promotion.
        var minimumDuration = TimeSpan.FromSeconds(15);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var completed = 0;
        do
        {
            WithObserver(observer =>
            {
                observer.Collection.RegisterLogger(observer);
                try
                {
                    observer.VerifyProtocol(observer.Collection);
                }
                catch (Exception failure)
                {
                    failure.Data["CanaryCompletedOwners"] = completed;
                    failure.Data["CanaryElapsedMilliseconds"] = elapsed.ElapsedMilliseconds;
                    throw;
                }
            });
            completed++;
            await Task.Yield();
        } while (completed < 256 || elapsed.Elapsed < minimumDuration);

        Assert.IsTrue(completed >= 256);
        Assert.IsTrue(elapsed.Elapsed >= minimumDuration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyRepeatedOwners()
    {
        for (var iteration = 0; iteration < 256; iteration++)
        {
            WithObserver(observer =>
            {
                observer.Collection.RegisterLogger(observer);
                observer.VerifyProtocol(observer.Collection);
            });
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeCacheArguments_PreserveRepeatedAndDistinctListMembers(bool distinct)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        ObserveList(distinct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ObserveList(bool distinct)
    {
        WithObserver(observer =>
        {
            var owner = observer.Collection;
            owner.RegisterLogger(observer);
            observer.VerifyProtocol(owner);
            var directory = Path.Combine(TestTempRoot.Current, nameof(NativeConditionCacheObservationTests), Guid.NewGuid().ToString("N"));
            var first = ProjectRootElement.Create(owner);
            first.FullPath = Path.Combine(directory, "first.props");
            var second = ProjectRootElement.Create(owner);
            second.FullPath = Path.Combine(directory, "second.props");
            var root = ProjectRootElement.Create(owner);
            var paths = new[] { first.FullPath, distinct ? second.FullPath : first.FullPath };
            root.AddImportGroup().Condition = "Exists('" + string.Join(";", paths.Select(ProjectCollection.Escape)) + "')";
            root.FullPath = Path.Combine(directory, "list.proj");
            observer.Begin();
            var project = Project.FromProjectRootElement(root, observer.Options(new Dictionary<string, string>(), owner));
            observer.Bind(project);
            var invocation = observer.Executions(root.ImportGroups.Single()).Single().Invocations.Single();
            Assert.IsTrue(invocation.Result);
            Assert.HasCount(0, invocation.Inputs);
            CollectionAssert.AreEqual(paths, invocation.Arguments.ToArray(), "Native list multiplicity must survive observation without deduplication.");
            owner.UnloadProject(project);
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void UnregisteredWeakTarget_RefusesAtBoundaryOrNativeLookup(bool duringEvaluation, bool liveForeign)
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        VerifyUnregisteredTarget(duringEvaluation, liveForeign);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyUnregisteredTarget(bool duringEvaluation, bool liveForeign)
    {
        var directory = Path.Combine(TestTempRoot.Current, nameof(NativeConditionCacheObservationTests), Guid.NewGuid().ToString("N"));
        NativeConditionEvidence? observer = null;
        ProjectCollection? foreign = null;
        var retirementAttempted = false;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(directory);
            observer = new NativeConditionEvidence();
            var owner = observer.Collection;
            foreign = new ProjectCollection();
            owner.RegisterLogger(observer);
            observer.VerifyProtocol(owner);
            var victim = ProjectRootElement.Create(owner);
            victim.FullPath = Path.Combine(directory, "victim.props");
            var foreignRoot = ProjectRootElement.Create(foreign);
            foreignRoot.FullPath = victim.FullPath;
            var replacement = new WeakReference<ProjectRootElement>(foreignRoot);
            if (!liveForeign) replacement.SetTarget(null!);
            var cacheProperty = typeof(ProjectCollection).GetProperty("ProjectRootElementCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cache = cacheProperty.GetValue(owner)!;
            var weak = cache.GetType().GetField("_weakCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
            var dictionary = (Dictionary<string, WeakReference<ProjectRootElement>>)weak.GetType()
                .GetField("_dictionary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(weak)!;
            NativeConditionProtocolException failure;
            if (duringEvaluation)
            {
                var triggerPath = Path.Combine(directory, "trigger.props");
                File.WriteAllText(triggerPath, "<Project />");
                var root = ProjectRootElement.Create(owner);
                root.AddImport(triggerPath);
                root.AddImportGroup().Condition = "Exists('" + ProjectCollection.Escape(victim.FullPath) + "')";
                root.FullPath = Path.Combine(directory, "owner.proj");
                var injected = false;
                owner.ProjectAdded += (_, message) =>
                {
                    if (message.ProjectRootElement.FullPath != triggerPath) return;
                    dictionary[victim.FullPath] = replacement;
                    injected = true;
                };
                observer.Begin();
                var options = observer.Options(new Dictionary<string, string>(), owner);
                failure = Assert.ThrowsExactly<NativeConditionProtocolException>(() => Project.FromProjectRootElement(root, options));
                Assert.IsTrue(injected, "The invalid weak target must arrive after phase checks and before the native Exists lookup.");
            }
            else
            {
                dictionary[victim.FullPath] = replacement;
                failure = Assert.ThrowsExactly<NativeConditionProtocolException>(observer.Begin);
            }
            Assert.AreEqual(NativeConditionProtocolFailure.CacheObserver, failure.Failure);
            // Inspect preserved storage without re-entering its comparer: the
            // interrupted native scope still correctly refuses this poisoned entry.
            Assert.AreSame(replacement, dictionary.Single(pair => string.Equals(pair.Key, victim.FullPath, StringComparison.Ordinal)).Value,
                "Refusal must precede native dead-reference removal.");
            // Evidence.Dispose also retires its collection when cache retirement fails.
            retirementAttempted = true;
            var cleanup = Assert.ThrowsExactly<AggregateException>(observer.Dispose);
            Assert.IsTrue(cleanup.InnerExceptions.Any(error => error is NativeConditionProtocolException { Failure: NativeConditionProtocolFailure.CacheObserver }));
            Assert.AreSame(replacement, dictionary.Single(pair => string.Equals(pair.Key, victim.FullPath, StringComparison.Ordinal)).Value,
                "Cleanup must not overwrite unknown cache state.");
            GC.KeepAlive(foreignRoot);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Unregistered native target cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => { if (!retirementAttempted) observer?.Dispose(); }),
                foreign,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(directory)));
        }
    }

    [TestMethod]
    public void CachedFunctionArgument_IsObservedOnceAcrossRepeatedNativeEvaluations()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        ObserveRepeatedly();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ObserveRepeatedly()
    {
        WithObserver(observer =>
        {
            var owner = observer.Collection;
            owner.RegisterLogger(observer);
            observer.VerifyProtocol(owner);
            var root = ProjectRootElement.Create(owner);
            var projectPath = Path.Combine(TestTempRoot.Current, nameof(NativeConditionCacheObservationTests), "cached.proj");
            var group = root.AddImportGroup();
            group.Condition = "Exists('$([System.IO.Path]::Combine(`" + ProjectCollection.Escape(Path.GetDirectoryName(projectPath)!)
                + "`, `cached.proj`))') And ('true' == 'true' Or Exists('$([System.IO.Path]::Combine(`unused`, `unqueried.proj`))'))";
            root.FullPath = projectPath;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                observer.Begin();
                var project = Project.FromProjectRootElement(root, observer.Options(new Dictionary<string, string>(), owner));
                Exception? primary = null;
                try
                {
                    observer.Bind(project);
                    var execution = observer.Executions(root.ImportGroups.Single()).Single();
                    Assert.HasCount(1, execution.Invocations);
                    Assert.IsTrue(execution.Invocations[0].Result);
                    Assert.HasCount(0, execution.Inputs, "A real cache hit must not manufacture filesystem evidence.");
                    CollectionAssert.AreEqual(new[] { root.FullPath }, execution.Invocations[0].Arguments.ToArray(),
                        "Observe the executed original argument exactly once; short-circuited functions supply no authority.");
                }
                catch (Exception failure) { primary = failure; throw; }
                finally
                {
                    OwnedResourceCleanup.DisposePreservingFailure("Native repeated evaluation cleanup failed.", primary,
                        OwnedResourceCleanup.FromAction(() => owner.UnloadProject(project)));
                }
            }
        });
    }

    [TestMethod]
    public void ForeignCollection_IsRefusedWithoutChangingItsNativeEvaluation()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        VerifyForeignOwner();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyForeignOwner()
    {
        NativeConditionEvidence? observer = null;
        ProjectCollection? foreign = null;
        Exception? primary = null;
        try
        {
            observer = new NativeConditionEvidence();
            foreign = new ProjectCollection();
            var failure = Assert.ThrowsExactly<NativeConditionProtocolException>(() => observer.Options(new Dictionary<string, string>(), foreign));
            Assert.AreEqual(NativeConditionProtocolFailure.CacheObserver, failure.Failure);
            var root = ProjectRootElement.Create(foreign);
            root.FullPath = Path.Combine(TestTempRoot.Current, nameof(NativeConditionCacheObservationTests), "foreign.proj");
            root.AddProperty("Unchanged", "native");
            var project = Project.FromProjectRootElement(root, new Microsoft.Build.Definition.ProjectOptions { ProjectCollection = foreign });
            Assert.AreEqual("native", project.GetPropertyValue("Unchanged"));
            foreign.UnloadProject(project);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Foreign native collection cleanup failed.", primary, foreign, observer); }
    }

    [TestMethod]
    public void NativeCacheReplacement_RefusesAndDoesNotRestoreOverReplacement()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        VerifyReplacement();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyReplacement()
    {
        NativeConditionEvidence? observer = null;
        var retirementAttempted = false;
        Exception? primary = null;
        try
        {
            observer = new NativeConditionEvidence();
            var owner = observer.Collection;
            // The public native API replaces its weak cache; no private mutation test hook is needed.
            owner.UnloadAllProjects();
            var failure = Assert.ThrowsExactly<NativeConditionProtocolException>(observer.Begin);
            Assert.AreEqual(NativeConditionProtocolFailure.CacheObserver, failure.Failure);
            var cacheProperty = typeof(ProjectCollection).GetProperty("ProjectRootElementCache", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(cacheProperty);
            var cache = cacheProperty.GetValue(owner);
            Assert.IsNotNull(cache);
            var weakField = cache.GetType().GetField("_weakCache", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(weakField);
            var replacement = weakField.GetValue(cache);
            // Evidence.Dispose also retires its collection when cache retirement fails.
            retirementAttempted = true;
            var cleanup = Assert.ThrowsExactly<AggregateException>(observer.Dispose);
            Assert.IsTrue(cleanup.InnerExceptions.Any(error => error is NativeConditionProtocolException { Failure: NativeConditionProtocolFailure.CacheObserver }));
            Assert.AreSame(replacement, weakField.GetValue(cache), "Disposal must preserve the unowned replacement instead of restoring over it.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native cache replacement cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => { if (!retirementAttempted) observer?.Dispose(); }));
        }
    }

    private static void WithObserver(Action<NativeConditionEvidence> body)
    {
        NativeConditionEvidence? observer = null;
        Exception? primary = null;
        try
        {
            observer = new NativeConditionEvidence();
            body(observer);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native cache observer cleanup failed.", primary, observer); }
    }

}
