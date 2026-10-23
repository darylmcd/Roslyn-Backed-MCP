using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class OwnedTestWorkspacePathPolicyTests
{
    [TestMethod]
    public async Task AncestorConfiguration_GrantsOnlyExactConfiguredCoordinates()
    {
        var sandbox = Path.Combine(TestTempRoot.Current, nameof(OwnedTestWorkspacePathPolicyTests), Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(sandbox, "fixture");
        Directory.CreateDirectory(owned);
        try
        {
            var policy = new OwnedTestWorkspacePathPolicy([owned]);
            var config = Path.Combine(sandbox, ".editorconfig");
            await File.WriteAllTextAsync(config, "root = true\n");
            await policy.ValidateAsync("fixture", [config, Path.Combine(owned, "Source.cs")], CancellationToken.None);
            foreach (var denied in new[] { sandbox, Path.Combine(sandbox, "other.editorconfig"),
                Path.Combine(sandbox, "Directory.Build.props"), Path.Combine(sandbox, "sibling", ".editorconfig") })
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => policy.ValidateAsync("fixture", [denied], CancellationToken.None));
            File.Delete(config);
            Directory.CreateDirectory(config);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => policy.ValidateAsync("fixture", [Path.Combine(config, "child")], CancellationToken.None));
            Directory.Delete(config);
            await policy.ValidateAsync("fixture", [config], CancellationToken.None);
            await File.WriteAllTextAsync(config, "root = false\n");
            await policy.ValidateAsync("fixture", [config], CancellationToken.None);
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(sandbox); }
    }

    [TestMethod]
    public async Task ConfiguredAncestor_RechecksPhysicalIdentityAfterNamespaceReplacement()
    {
        var sandbox = Path.Combine(TestTempRoot.Current, nameof(OwnedTestWorkspacePathPolicyTests), Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(sandbox, "parent");
        var target = Path.Combine(sandbox, "target");
        var owned = Path.Combine(parent, "fixture");
        Directory.CreateDirectory(owned);
        Directory.CreateDirectory(target);
        try
        {
            var config = Path.Combine(parent, ".editorconfig");
            await File.WriteAllTextAsync(config, "root = true\n");
            await File.WriteAllTextAsync(Path.Combine(target, ".editorconfig"), "root = true\n");
            var policy = new OwnedTestWorkspacePathPolicy([owned]);
            await policy.ValidateAsync("fixture", [config], CancellationToken.None);
            TestFixtureFileSystem.DeleteDirectoryIfExists(parent);
            Assert.IsTrue(TestFixtureFileSystem.TryCreateDirectoryLink(parent, target));
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => policy.ValidateAsync("fixture", [config], CancellationToken.None));
        }
        finally { TestFixtureFileSystem.DeleteDirectoryIfExists(sandbox); }
    }
}
