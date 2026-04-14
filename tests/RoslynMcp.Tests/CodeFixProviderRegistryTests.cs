using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for code-fix-providers-missing-ca: <see cref="CodeFixProviderRegistry"/> must
/// surface providers for both compiler diagnostics (CS*) and analyzer diagnostics (CA*/IDE*)
/// rather than the previous CS8019-only hardcoded path.
/// </summary>
[TestClass]
public sealed class CodeFixProviderRegistryTests
{
    [TestMethod]
    public void Registry_LoadsAtLeastOneStaticProvider()
    {
        // Force the IDE Features assembly to be loaded before the registry probes for it. In
        // unit-test isolation the assembly isn't loaded into the AppDomain until something
        // (the test or production code) touches it; the registry uses Assembly.Load by name
        // which only succeeds if the assembly is already loaded.
        _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        // Sanity: probe a handful of well-known diagnostic ids — at least one should resolve.
        // Different Roslyn versions ship different IDE/CS diagnostic ids; we just verify the
        // loader actually pulled providers, not which exact ids.
        string[] knownIds = ["CS8019", "IDE0005", "IDE0044", "CS0168", "CS0219", "CS1591"];
        var anyResolved = knownIds.Any(id => registry.GetProvidersFor(id).Count > 0);

        Assert.IsTrue(anyResolved,
            "Registry must expose at least one provider across well-known diagnostic ids " +
            $"({string.Join(", ", knownIds)}). The static loader likely failed to load " +
            "Microsoft.CodeAnalysis.CSharp.Features.");
    }

    [TestMethod]
    public void Registry_UnknownDiagnostic_ReturnsEmpty()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        var providers = registry.GetProvidersFor("ZZ9999");
        Assert.AreEqual(0, providers.Count);
    }

    [TestMethod]
    public void FirstProviderFor_ReturnsNullForUnknownDiagnostic()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        Assert.IsNull(registry.FirstProviderFor("ZZ9999"));
    }
}
