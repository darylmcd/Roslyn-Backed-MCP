using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

public sealed partial class SolutionFileMutationEscapingTests
{
    public static IEnumerable<object[]> XmlReferenceLineCases =>
        from ending in new[] { "crlf", "lf", "cr" }
        from operation in new[] { "remove", "update", "add" }
        from unicode in new[] { false, true }
        select new object[] { ending, operation, unicode };

    [TestMethod]
    [DynamicData(nameof(XmlReferenceLineCases))]
    public Task XmlProjectReferencePatch_PreservesLineEndingsAndUnchangedBytes(string ending, string operation, bool unicode)
        => WithFixtureAsync(async fixture =>
        {
            var newline = LineEnding(ending);
            var encoding = DefinitionEncoding(unicode);
            var peerPath = Path.Combine(fixture.Root, "Peer.csproj");
            await File.WriteAllTextAsync(peerPath, "<Project />");
            const string entry = "<ProjectReference Include=\"../Peer.csproj\" />";
            var before = string.Join(newline, "<Project>", "  <!-- unchanged café &amp; comment -->",
                "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>",
                operation == "add" ? "  <!-- add here -->" : "  <ItemGroup>" + entry + "</ItemGroup>", "</Project>", "");
            var bytes = EncodeDefinition(before, encoding);
            await File.WriteAllBytesAsync(fixture.ProjectPath, bytes);
            var peer = ProjectId.CreateNewId();
            var original = fixture.Solution.AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer",
                LanguageNames.CSharp, filePath: peerPath));
            if (operation != "add") original = original.AddProjectReference(fixture.Owner, new ProjectReference(peer));
            var modified = operation == "add" ? original : original.RemoveProjectReference(fixture.Owner,
                original.GetProject(fixture.Owner)!.ProjectReferences.Single());
            if (operation != "remove") modified = modified.AddProjectReference(fixture.Owner, new ProjectReference(peer, ["global", "library"], true));
            var initial = Evaluate(fixture.ProjectPath, bytes, "ProjectReference");
            Assert.HasCount(operation == "add" ? 0 : 1, initial);
            if (initial.Length != 0) Assert.AreEqual(peerPath, initial[0].Path);

            var rendered = await RenderAsync(fixture, original, modified);
            var items = Evaluate(fixture.ProjectPath, rendered, "ProjectReference");
            Assert.HasCount(operation == "remove" ? 0 : 1, items);
            const string updated = "<ProjectReference Include=\"../Peer.csproj\"><Aliases>global,library</Aliases><EmbedInteropTypes>true</EmbedInteropTypes></ProjectReference>";
            var expected = operation switch
            {
                "remove" => before.Replace(entry, "", StringComparison.Ordinal),
                "update" => before.Replace(entry, updated, StringComparison.Ordinal),
                _ => before.Replace("</Project>", "  <ItemGroup>" + updated.Replace("../Peer.csproj",
                    Escape(Path.GetRelativePath(Path.GetDirectoryName(fixture.ProjectPath)!, peerPath)), StringComparison.Ordinal)
                    + "</ItemGroup>" + newline + "</Project>", StringComparison.Ordinal)
            };
            CollectionAssert.AreEqual(EncodeDefinition(expected, encoding), rendered,
                "Only the reviewed XML fragment may change; authored encoding, BOM, comments and line endings remain exact.");
            if (items.Length != 0)
            {
                Assert.AreEqual(peerPath, items[0].Path);
                Assert.AreEqual("global,library", items[0].Aliases);
                Assert.AreEqual("true", items[0].EmbedInteropTypes);
            }
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(fixture.ProjectPath));
        });

    public static IEnumerable<object[]> SolutionLineCases =>
        from ending in new[] { "crlf", "lf", "cr" }
        from unicode in new[] { false, true }
        select new object[] { ending, unicode };

    [TestMethod]
    [DynamicData(nameof(SolutionLineCases))]
    public Task SlnxMembershipPatch_PreservesLineEndingsAndUnchangedBytes(string ending, bool unicode)
        => WithFixtureAsync(async fixture =>
        {
            var newline = LineEnding(ending);
            var encoding = DefinitionEncoding(unicode);
            var path = Path.Combine(fixture.Root, "Lines.slnx");
            var peerPath = Path.Combine(fixture.Root, "Peer.csproj");
            await File.WriteAllTextAsync(peerPath, "<Project />");
            var text = string.Join(newline, "<Solution>", "  <!-- unchanged café -->", "  <Folder Name=\"/Owned/\">",
                "    <Project Path=\"owner/Owner.csproj\" />", "    <Project Path=\"Peer.csproj\" />", "  </Folder>", "</Solution>", "");
            var bytes = EncodeDefinition(text, encoding);
            await File.WriteAllBytesAsync(path, bytes);
            await AssertNativeSolutionContainsAsync(fixture.Root, path, "Peer.csproj");
            using var workspace = new AdhocWorkspace();
            var peer = ProjectId.CreateNewId();
            var original = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: path))
                .AddProject(ProjectInfo.Create(fixture.Owner, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: fixture.ProjectPath))
                .AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer", LanguageNames.CSharp, filePath: peerPath));
            var removed = await RenderMembershipAsync(original, original.RemoveProject(peer), path);
            CollectionAssert.AreEqual(EncodeDefinition(text.Replace("<Project Path=\"Peer.csproj\" />", "", StringComparison.Ordinal), encoding), removed);
            var ownerOnly = original.RemoveProject(peer);
            await File.WriteAllBytesAsync(path, removed);
            var added = await RenderMembershipAsync(ownerOnly, original, path);
            var expected = encoding.GetString(removed.AsSpan(encoding.GetPreamble().Length)).Replace("</Solution>",
                "  <Project Path=\"Peer.csproj\" />" + newline + "</Solution>", StringComparison.Ordinal);
            CollectionAssert.AreEqual(EncodeDefinition(expected, encoding), added);
            CollectionAssert.AreEqual(removed, await File.ReadAllBytesAsync(path), "Rendering must not publish the addition.");
        });

    public static IEnumerable<object[]> TextSolutionLineCases =>
        from line in SolutionLineCases
        from operation in new[] { "remove", "transport" }
        select new object[] { line[0], line[1], operation };

    [TestMethod]
    [DynamicData(nameof(TextSolutionLineCases))]
    public Task TextSolutionMembershipOrTransport_UsesNativeLiteralCoordinatesAndLineEndings(string ending, bool unicode, string operation)
        => WithFixtureAsync(async fixture =>
        {
            var newline = LineEnding(ending);
            var encoding = DefinitionEncoding(unicode);
            var path = Path.Combine(fixture.Root, "Lines.sln");
            var peerPath = Path.Combine(fixture.Root, "nested", "Peer%3BExtra.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(peerPath)!);
            await File.WriteAllTextAsync(peerPath, "<Project />");
            const string ownerId = "{11111111-1111-1111-1111-111111111111}";
            const string peerId = "{22222222-2222-2222-2222-222222222222}";
            const string projectType = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
            var ownerEntry = "Project(\"" + projectType + "\") = \"Owner\", \"owner\\Owner.csproj\", \"" + ownerId + "\"";
            var peerEntry = "Project(\"" + projectType + "\") = \"Peer\", \"nested\\Peer%3BExtra.csproj\", \"" + peerId + "\"";
            var text = string.Join(newline, "Microsoft Visual Studio Solution File, Format Version 12.00", "# Visual Studio Version 17",
                "# unchanged café", ownerEntry, "EndProject", peerEntry, "EndProject", "Global",
                "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution", "\t\tDebug|Any CPU = Debug|Any CPU",
                "\tEndGlobalSection", "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
                "\t\t" + peerId + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU", "\tEndGlobalSection", "EndGlobal", "");
            var bytes = EncodeDefinition(text, encoding);
            await File.WriteAllBytesAsync(path, bytes);
            await AssertNativeSolutionContainsAsync(fixture.Root, path, "nested/Peer%3BExtra.csproj");
            CollectionAssert.Contains(NativeTextSolutionProjects(path), peerPath,
                "The installed native solution parser must select the literal physical path before renderer assertions.");
            using var workspace = new AdhocWorkspace();
            var peer = ProjectId.CreateNewId();
            var original = workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), filePath: path))
                .AddProject(ProjectInfo.Create(fixture.Owner, VersionStamp.Create(), "Owner", "Owner", LanguageNames.CSharp, filePath: fixture.ProjectPath))
                .AddProject(ProjectInfo.Create(peer, VersionStamp.Create(), "Peer", "Peer", LanguageNames.CSharp, filePath: peerPath));
            if (operation == "remove")
            {
                var removed = await RenderMembershipAsync(original, original.RemoveProject(peer), path);
                var expected = text.Replace(peerEntry + newline + "EndProject" + newline, "", StringComparison.Ordinal)
                    .Replace("\t\t" + peerId + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU" + newline, "", StringComparison.Ordinal);
                CollectionAssert.AreEqual(EncodeDefinition(expected, encoding), removed);
                CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
                return;
            }

            var destination = Path.Combine(fixture.Root, "private-stage");
            var mappedOwner = Path.Combine(destination, "owner", "Owner.csproj");
            var mappedPeer = Path.Combine(destination, "nested", "Peer%3BExtra.csproj");
            var mappings = new Dictionary<string, string>(FileSystemPath.Comparer)
            { [fixture.ProjectPath] = mappedOwner, [peerPath] = mappedPeer };
            var seen = new HashSet<string>(FileSystemPath.Comparer);
            var relocated = SolutionFileMutationPlanner.RenderSolutionCoordinates(path, bytes, value =>
            {
                Assert.IsTrue(mappings.TryGetValue(value, out var mapped), "Transport must receive the actual native literal coordinate: " + value);
                seen.Add(value);
                return mapped!;
            });
            Assert.HasCount(2, seen);
            var relocatedExpected = text.Replace("owner\\Owner.csproj", mappedOwner, StringComparison.Ordinal)
                .Replace("nested\\Peer%3BExtra.csproj", mappedPeer, StringComparison.Ordinal);
            CollectionAssert.AreEqual(EncodeDefinition(relocatedExpected, encoding), relocated);
            Directory.CreateDirectory(destination);
            var stagedPath = Path.Combine(destination, "Lines.sln");
            await File.WriteAllBytesAsync(stagedPath, relocated);
            CollectionAssert.Contains(NativeTextSolutionProjects(stagedPath), mappedPeer);
            await AssertNativeSolutionContainsAsync(fixture.Root, stagedPath, "Peer%3BExtra.csproj");
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        });

    private static string LineEnding(string ending) => ending switch
    { "crlf" => "\r\n", "lf" => "\n", "cr" => "\r", _ => throw new AssertFailedException("Unknown line ending.") };

    private static Encoding DefinitionEncoding(bool unicode) => unicode ? new UnicodeEncoding(false, true) : new UTF8Encoding(false);
    private static byte[] EncodeDefinition(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static async Task<byte[]> RenderMembershipAsync(Solution original, Solution modified, string path)
    {
        var mutations = await SolutionFileMutationPlanner.CreateAsync(original, modified,
            async (file, ct) => File.Exists(file) ? await File.ReadAllBytesAsync(file, ct) : null,
            new CompilerReferenceAuthority(new Dictionary<string, CompilerReferenceAuthority.ProjectEdges>()),
            includeDocuments: false, allowVirtualPaths: false, CancellationToken.None);
        return mutations.Single(mutation => FileSystemPath.Comparer.Equals(mutation.Path, path)).After!;
    }

    private static async Task AssertNativeSolutionContainsAsync(string root, string path, string expected)
    {
        var native = await new DotnetCommandRunner().RunAsync(root, path, ["sln", path, "list"], CancellationToken.None);
        Assert.IsTrue(native.Succeeded, native.StdErr);
        StringAssert.Contains(native.StdOut.Replace('\\', '/'), expected);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] NativeTextSolutionProjects(string path) => Microsoft.Build.Construction.SolutionFile.Parse(path)
        .ProjectsInOrder.Where(project => project.ProjectType != Microsoft.Build.Construction.SolutionProjectType.SolutionFolder)
        .Select(project => project.AbsolutePath).ToArray();
}
