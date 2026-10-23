using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.FileSystem;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

// Run native function expansion in an isolated child with explicit function permissions.
// The custom native property function is an adversarial callback, not a claim
// that built-in String/Path/File functions expose the private observer.
[TestClass]
[DoNotParallelize]
public sealed class NativeConditionExpansionOriginTests
{
    [TestMethod]
    public Task EmptyOuterOperand_CannotBorrowExpansionQuery() =>
        NativeConditionProcessTest.RunAsync(typeof(NativeConditionExpansionOriginTests),
            nameof(EmptyOuterOperand_CannotBorrowExpansionQuery),
            new Dictionary<string, string?> { ["MSBUILDENABLEALLPROPERTYFUNCTIONS"] = "1" }, static () =>
            {
                RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
                VerifyExpansionOrigin(false, false);
                VerifyExpansionOrigin(true, false);
                VerifyExpansionOrigin(true, true);
            });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyExpansionOrigin(bool importCondition, bool cachedResult)
    {
        var observer = new NativeConditionEvidence();
        Exception? primary = null;
        try
        {
            var owner = observer.Collection;
            owner.RegisterLogger(observer);
            observer.VerifyProtocol(owner);
            var directory = Path.Combine(TestTempRoot.Current, nameof(NativeConditionExpansionOriginTests), Guid.NewGuid().ToString("N"));
            var missing = Path.Combine(directory, "missing.flag");
            Assert.IsFalse(File.Exists(missing));
            Assert.IsFalse(Directory.Exists(missing));
            var fileSystem = (MSBuildFileSystemBase)typeof(NativeConditionEvidence)
                .GetField("_fileSystem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(observer)!;
            var cacheProperty = typeof(ProjectCollection).GetProperty("ProjectRootElementCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cache = cacheProperty.GetValue(owner)!;
            var weak = cache.GetType().GetField("_weakCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
            var dictionary = (Dictionary<string, WeakReference<ProjectRootElement>>)weak.GetType()
                .GetField("_dictionary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(weak)!;
            var callbackCalls = 0;
            var output = string.Empty;
            var queryWasFalse = false;
            var cacheMiss = false;
            ExpansionOriginNativeCallback.Body = () =>
            {
                callbackCalls++;
                if (importCondition) cacheMiss = !dictionary.TryGetValue(missing, out _);
                queryWasFalse = !fileSystem.FileOrDirectoryExists(missing);
                return output;
            };
            try
            {
                var callbackType = typeof(ExpansionOriginNativeCallback).AssemblyQualifiedName!;
                var expression = "Exists('$([System.String]::Copy($([" + callbackType + "]::Execute())))')";
                var root = ProjectRootElement.Create(owner);
                if (importCondition)
                    root.AddImportGroup().Condition = expression;
                else
                {
                    var group = root.AddPropertyGroup();
                    group.Condition = expression;
                    group.AddProperty("Selected", "true");
                }
                root.FullPath = Path.Combine(directory, "origin.proj");
                if (cachedResult) output = root.FullPath;
                observer.Begin();
                var project = Project.FromProjectRootElement(root, observer.Options(new Dictionary<string, string>(), owner));
                observer.Bind(project);
                Assert.AreEqual(1, callbackCalls, "The callback must execute once during the original native operand expansion.");
                Assert.IsTrue(queryWasFalse, "The actual private adapter must observe the missing expansion input.");
                if (importCondition) Assert.IsTrue(cacheMiss, "The adversarial expansion lookup must use this exact owned cache.");
                ProjectElement element = importCondition ? root.ImportGroups.Single() : root.PropertyGroups.Single();
                var invocation = observer.Executions(element).Single().Invocations.Single();
                Assert.AreEqual(cachedResult, invocation.Result, "The actual outer operand must be empty/false or the real cached root/true.");
                Assert.HasCount(1, invocation.Inputs,
                    "Retain expansion evidence so replay can refuse it rather than silently discarding it.");
                Assert.AreEqual(missing, invocation.Inputs[0].Path);
                Assert.IsFalse(invocation.Inputs[0].Exists);
                Assert.IsTrue(invocation.Inputs[0].ArgumentExpansion);
                Assert.HasCount(cachedResult ? 1 : 0, invocation.Arguments,
                    "Current positive native-callsite filtering excludes expansion lookup and retains only the actual cached root argument.");
                if (cachedResult) Assert.AreEqual(root.FullPath, invocation.Arguments[0]);

                var failure = Assert.ThrowsExactly<NativeConditionProtocolException>(() => BuildPathRoles.Capture(project, observer),
                    "An expansion query must not be admitted as the original path in either empty-argument or cached-result replay.");
                Assert.AreEqual(NativeConditionProtocolFailure.OperandOrigin, failure.Failure);
            }
            finally { ExpansionOriginNativeCallback.Body = null; }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native expansion-origin observer cleanup failed.", primary, observer);
        }
    }
}

public static class ExpansionOriginNativeCallback
{
    internal static Func<string>? Body;
    public static string Execute() => (Body ?? throw new InvalidOperationException("No owned callback was installed."))();
}
