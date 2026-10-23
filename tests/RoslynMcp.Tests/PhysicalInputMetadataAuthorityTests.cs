using System.Globalization;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalInputMetadataAuthorityTests
{
    [TestMethod]
    [DataRow("2026-10-22T12:34:56.1234567Z")]
    [DataRow("1965-04-03T02:01:00.7654321Z")]
    public async Task Timestamp_PreservesBclUtcPrecisionWithoutReadingContent(string value)
    {
        await InFixtureAsync(async root =>
        {
            var file = Path.Combine(root, "input.bin");
            await File.WriteAllTextAsync(file, "metadata fixture");
            File.SetLastWriteTimeUtc(file, DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
            var expected = File.GetLastWriteTimeUtc(file);
            var actual = PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(file, CancellationToken.None);
            Assert.AreEqual(expected, actual);
            Assert.AreEqual(DateTimeKind.Utc, actual!.Value.Kind);
            Assert.IsNull(PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(Path.Combine(root, "missing"), CancellationToken.None));
            Assert.IsNull(PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(Path.Combine(root, "missing-parent", "missing"), CancellationToken.None));
            Assert.IsNull(PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(root, CancellationToken.None), "A directory does not become a cached file reference.");
        });
    }

    [TestMethod]
    public async Task WindowsExclusiveContentWriter_DoesNotHideFileMetadata()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This control qualifies Windows mandatory sharing behavior.");
        await InFixtureAsync(async root =>
        {
            var file = Path.Combine(root, "restore-artifact.json");
            await File.WriteAllTextAsync(file, "exclusive writer fixture");
            var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Exception? primary = null;
            try
            {
                Assert.ThrowsExactly<IOException>(() => File.ReadAllBytes(file));
                Assert.AreEqual(File.GetLastWriteTimeUtc(file), PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(file, CancellationToken.None),
                    "Polling metadata must remain observable while content access is exclusively held.");
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Exclusive metadata writer cleanup failed.", primary, writer); }
        });
    }

    [TestMethod]
    public async Task LeafAlias_IsRefusedBeforeFollowingItsTarget()
    {
        await InFixtureAsync(async root =>
        {
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "alias");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "preserve.txt"), "target sentinel");
            if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(link, target);
            else Directory.CreateSymbolicLink(link, target);
            Exception? aliasPrimary = null;
            try
            {
                Exception? refusal = null;
                try { _ = PhysicalInputReader.ReadFileLastWriteTimeUtcIfPresent(link, CancellationToken.None); }
                catch (Exception failure) { refusal = failure; }
                Assert.IsNotNull(refusal, "A linked leaf must not become a physical metadata observation.");
                Assert.AreEqual("target sentinel", await File.ReadAllTextAsync(Path.Combine(target, "preserve.txt")));
            }
            catch (Exception failure) { aliasPrimary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Metadata alias unlink failed.", aliasPrimary,
                    CleanupFailureCollector.FromAction(() => Directory.Delete(link)));
            }
        });
    }

    private static async Task InFixtureAsync(Func<string, Task> test)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalInputMetadataAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try { Directory.CreateDirectory(root); await test(root); }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Metadata fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
