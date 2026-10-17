using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureStructInitializationTests : ChangeSignatureTestBase
{

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_IndependentInitializersRetainConstruction()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + new SelectedStruct().Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var document = original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path);
        var represented = source.Replace(declaration,
            "public struct SelectedStruct { public int Value = 7; public SelectedStruct() { } }", StringComparison.Ordinal)
            .Replace("new SelectedStruct().Value", "default(SelectedStruct).Value", StringComparison.Ordinal)
            .Replace("new SelectedStruct(2).Value", "new SelectedStruct().Value", StringComparison.Ordinal);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(original.WithDocumentText(document.Id,
            Microsoft.CodeAnalysis.Text.SourceText.From(represented)), "SampleLib.SignatureProof"));
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("7|0", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    [DataRow("explicit")]
    [DataRow("implicit")]
    [DataRow("generic")]
    [DataRow("property")]
    public async Task ChangeSignaturePreview_LastStructParameter_DefaultInitializerRetainsEffects(string shape)
    {
        var declaration = shape == "property"
            ? "public struct SelectedStruct(int unused) { public int Value { get; set; } = SignatureProof.Trace(7); public int Other { get; set; } }"
            : "public struct SelectedStruct" + (shape == "generic" ? "<T>" : "")
                + "(int unused) { public int Value = SignatureProof.Trace(7); public int Other { get; set; } }";
        var type = shape == "generic" ? "SelectedStruct<string>" : "SelectedStruct";
        var zero = shape == "implicit" ? "new()" : "new " + type + "()";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { static string trace = \"\"; public static int Trace(int value) { trace += value; return value; } public static string Run() { "
            + type + " zero = " + zero + " { /*signature-dot*/ Other = Trace(3) }; var selected = new " + type
            + "(Trace(2)); return trace + \"|\" + zero.Value + \"|\" + zero.Other + \"|\" + selected.Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "37|0|3|7", 1);
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("multiline")]
    [DataRow("no-initializer")]
    public async Task ChangeSignaturePreview_LastStructParameter_PreservesConstructorAttributes(string shape)
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "[method: /*signature-method*/ Marker]" + (shape == "multiline" ? "\n" : " ")
            + "public struct SelectedStruct(int unused) { public int Value"
            + (shape == "no-initializer" ? " => 7;" : " = SignatureProof.Line();") + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\n[System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class MarkerAttribute : System.Attribute { }"
            + "\npublic static class SignatureProof { public static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line; public static string Run() => System.Linq.Enumerable.Count(typeof(SelectedStruct).GetConstructors(), c => c.IsDefined(typeof(MarkerAttribute), false)) + \"|\" + new SelectedStruct(2).Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        var expected = shape == "no-initializer" ? "1|7" : shape == "multiline" ? "1|3" : "1|2";
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var preview = await ChangeSignature.PreviewChangeSignatureAsync(workspace.WorkspaceId,
            SymbolLocator.BySource(path, shape == "multiline" ? 3 : 2, declaration[(declaration.LastIndexOf("\n", StringComparison.Ordinal) + 1)..].IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
            new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        var text = (await stored.Value.ModifiedSolution.GetDocument(original.Projects.Single(p => p.Name == "SampleLib").Documents.Single(d => d.FilePath == path).Id)!.GetTextAsync()).ToString();
        Assert.AreEqual(1, text.Split("/*signature-method*/", StringSplitOptions.None).Length - 1);
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual(expected, await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_SemicolonDeclaration()
    {
        var declaration = "public struct SelectedStruct(int unused);";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static bool Run() => new SelectedStruct(2).Equals(new SelectedStruct()); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "True", 1);
    }

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("generic")]
    [DataRow("private")]
    [DataRow("escaped")]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericDefaultConstruction(string shape)
    {
        var type = shape == "generic" ? "SelectedStruct<int>" : shape == "escaped" ? "@event.SelectedStruct" : "SelectedStruct";
        var declaration = "public struct SelectedStruct" + (shape == "generic" ? "<T>" : "") + "(int unused) { public int Value = 7; }";
        if (shape == "escaped") declaration = "namespace @event { " + declaration + " }";
        var allocationMethod = "public static long Allocated() { for (int i = 0; i < 10000; i++) _ = SignatureProof.Zero<" + type + ">(); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) _ = SignatureProof.Zero<" + type + ">(); return System.GC.GetAllocatedBytesForCurrentThread() - before; }";
        var selected = "new " + type + "(2).Value + \"|\" + SignatureProof.Zero<" + type + ">().Value";
        if (shape == "private")
        {
            declaration = "public class Owner { " + declaration.Replace("public struct", "private struct", StringComparison.Ordinal)
                + " public static string Proof() => " + selected + "; " + allocationMethod + " }";
            selected = "Owner.Proof()";
        }
        var source = (shape == "escaped" ? "namespace SampleLib {\n" : "namespace SampleLib;\n") + declaration
            + "\npublic struct OtherStruct { public int Value; public OtherStruct() { Value = 9; } } public class OtherClass { public int Value = 11; }"
            + "\npublic static class SignatureProof { " + (shape == "private" ? "" : allocationMethod) + " public static T Zero<T>() where T : new() => new T(); public static string Run() => "
            + selected + " + \"|\" + Zero<OtherStruct>().Value + \"|\" + Zero<OtherClass>().Value + \"|\" + " + (shape == "private" ? "Owner." : "") + "Allocated(); }" + (shape == "escaped" ? " }" : "");
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|9|11|0", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericInitializerRetainsEffects(bool structConstraint)
    {
        var declaration = "public struct SelectedStruct(int unused) : IValue { public int Value = 7; public int Other { get; set; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IValue { int Other { get; set; } } public struct OtherStruct : IValue { public int Other { get; set; } public OtherStruct() { Other = 9; } }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace() { trace += \"3\"; return 3; } public static T Zero<T>() where T : "
            + (structConstraint ? "struct, IValue" : "IValue, new()")
            + " => new T { /*signature-dot*/ Other = Trace() }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); var other = Zero<OtherStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + zero.Other + \"|\" + other.Other + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|3|3|33", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedIndexerRetainsEffects()
    {
        var declaration = "public struct SelectedStruct(int unused) : IChild { public int Value = 7; public Child this[int index] => SignatureProof.GetChild(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IChild { Child this[int index] { get; } } public class Child { public int A; public int B; }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Child child = new(); public static Child GetChild() { trace += \"H\"; return child; } static int Trace(int value) { trace += value; return value; } public static T Zero<T>() where T : IChild, new() => new T { [Trace(1)] = { A = Trace(3), B = Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + child.A + \"|\" + child.B + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_GenericReceiverReassignment(bool reference)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { SignatureProof.Mutate?.Invoke(); Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); } public interface IReceiver { int Calls { get; } string Take<T>(T bag) where T : IBag; }"
            + "\npublic static class SignatureProof { public static System.Action? Mutate; public " + (reference ? "class" : "struct") + " Receiver : IReceiver { public int Calls { get; private set; } public string Take<T>(T bag) where T : IBag { Calls++; return bag.Sum + \"|\" + Calls; } } public static string Call<T, R>() where T : IBag, new() where R : IReceiver, new() { var receiver = new R(); Mutate = () => receiver = new R(); var result = receiver.Take(new T { 3, 4 }); Mutate = null; return result + \"|\" + receiver.Calls; } public static string Run() => new SelectedStruct(2).Value + \"|\" + Call<SelectedStruct, Receiver>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, reference ? "7|7|1|0" : "7|7|1|1", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ReadonlyReceiverRetainsAddress()
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { SignatureProof.ChangeReceiver(); Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Receiver { public int Calls; public readonly int Take<T>(T value) where T : IBag => Calls; } static Receiver receiver; public static void ChangeReceiver() { receiver.Calls = 9; } public static int Call<T>(in Receiver target) where T : IBag, new() => target.Take(new T { 3, 4 }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Call<SelectedStruct>(in receiver); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|9", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_ConstructorAttributeBindings(bool multiline)
    {
        var declaration = "[method: global::SampleLib.Marker(Code = 8)]" + (multiline ? "\n" : " ") + "public struct SelectedStruct(int unused) { public int Value = 7; public sealed class MarkerAttribute : System.Attribute { public int Code; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\n[System.AttributeUsage(System.AttributeTargets.Constructor)] public sealed class MarkerAttribute : System.Attribute { public int Code; public int Line { get; } public MarkerAttribute([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { Line = line; } }"
            + "\npublic static class SignatureProof { public static string Run() { var attribute = (MarkerAttribute)System.Attribute.GetCustomAttribute(typeof(SelectedStruct).GetConstructors()[0], typeof(MarkerAttribute))!; return new SelectedStruct(2).Value + \"|\" + attribute.Code + \"|\" + attribute.Line; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), multiline ? 3 : 2,
            declaration.Split('\n')[^1].IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|8|2", 1);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("field-zero")]
    [DataRow("property-zero")]
    [DataRow("default-zero")]
    [DataRow("pure-zero")]
    [DataRow("static-effect")]
    [DataRow("static-empty")]
    [DataRow("effectful-zero")]
    [DataRow("field-nonzero")]
    [DataRow("property-nonzero")]
    [DataRow("attribute")]
    public async Task ChangeSignaturePreview_LastStructParameter_NeutralInitializationBoundary(string shape)
    {
        var members = shape switch
        {
            "field-zero" => "public int Value = 0;",
            "field-nonzero" => "public int Value = 7;",
            "property-nonzero" => "public int Value { get; set; } = 7;",
            "static-empty" => "public int Value = 0; static SelectedStruct() { }",
            "effectful-zero" => "public int Value = Zero(); private static int Zero() { SignatureProof.Trace += \"I\"; return 0; }",
            "property-zero" => "public int Value { get; set; } = 0;",
            "default-zero" => "public int Value = default(int);",
            "pure-zero" => "public int Value = Zero(); private static int Zero() => 0;",
            "static-effect" => "public int Value = 0; static SelectedStruct() { SignatureProof.Trace += \"S\"; }",
            _ => "public int Value;"
        };
        var declaration = (shape == "attribute" ? "[method: Marker] " : "")
            + "public struct SelectedStruct(int unused) { " + members + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic sealed class MarkerAttribute : System.Attribute { }"
            + "\npublic static class SignatureProof { public static string Trace = \"\"; public static string Run() { var first = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var before = Trace; var selected = new SelectedStruct(2); var middle = Trace; var last = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var attributes = System.Linq.Enumerable.Sum(typeof(SelectedStruct).GetConstructors(), c => c.GetCustomAttributes(typeof(MarkerAttribute), false).Length); return first.Value + \"|\" + selected.Value + \"|\" + last.Value + \"|\" + before + \"|\" + middle + \"|\" + Trace + \"|\" + attributes; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("positive-double")]
    [DataRow("negative-double")]
    [DataRow("decimal-scale")]
    [DataRow("nullable-value")]
    [DataRow("nullable-default")]
    [DataRow("nullable-source-value")]
    [DataRow("nullable-source-default")]
    [DataRow("nullable-lifted-default")]
    [DataRow("enum-zero")]
    [DataRow("default-struct")]
    public async Task ChangeSignaturePreview_LastStructParameter_TypedInitializationBoundary(string shape)
    {
        var member = shape switch
        {
            "positive-double" => "public double Value = 0.0D;",
            "negative-double" => "public double Value = -0.0D;",
            "decimal-scale" => "public decimal Value = 0.0M;",
            "nullable-value" => "public int? Value = 0;",
            "nullable-default" => "public int? Value = default(int?);",
            "nullable-source-value" => "public int? Value = Zero(); private static int? Zero() => 0;",
            "nullable-source-default" => "public int? Value = Zero(); private static int? Zero() => default(int?);",
            "nullable-lifted-default" => "public long? Value = Zero(); private static int? Zero() => default(int?);",
            "enum-zero" => "public Kind Value = Kind.Zero;",
            _ => "public Payload Value = default(Payload);"
        };
        var value = shape switch
        {
            "positive-double" or "negative-double" => "System.BitConverter.DoubleToInt64Bits(value.Value).ToString()",
            "decimal-scale" => "string.Join(\",\", decimal.GetBits(value.Value))",
            "nullable-value" or "nullable-default" or "nullable-source-value" or "nullable-source-default" or "nullable-lifted-default" => "value.Value.HasValue.ToString()",
            "enum-zero" => "((int)value.Value).ToString()",
            _ => "value.Value.Inner.ToString()"
        };
        var declaration = "public struct SelectedStruct(int unused) { " + member + " }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic enum Kind { Zero } public struct Payload { public int Inner; }"
            + "\npublic static class SignatureProof { private static string Show(SelectedStruct value) => " + value
            + "; public static string Run() { var first = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); var selected = new SelectedStruct(2); var last = System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\"); return Show(first) + \"|\" + Show(selected) + \"|\" + Show(last); } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ExistingZeroConstructor_PreservesBothPaths()
    {
        await using var workspace = CreateSingleProjectWorkspace();
        var path = workspace.GetPath("SampleLib", "SignatureProof.cs");
        var declaration = "public struct SelectedStruct(int unused) { public int Value; public SelectedStruct() : this(0) { Value = 3; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + new SelectedStruct().Value; }";
        await File.WriteAllTextAsync(path, source);
        await workspace.LoadAsync(CancellationToken.None);
        var original = Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(original, "SampleLib.SignatureProof"));
        var probe = new SignatureStoreProbe(Services.PreviewStore);
        var service = new ChangeSignatureService(Services.WorkspaceManager, probe, Services.RefactoringService);
        var preview = await service.PreviewChangeSignatureAsync(workspace.WorkspaceId,
                SymbolLocator.BySource(path, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1),
                new ChangeSignatureRequest("remove", Name: "unused"), CancellationToken.None);
        Assert.AreEqual(1, probe.StoreCalls);
        Assert.AreSame(original, Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId));
        Assert.AreEqual(source, await File.ReadAllTextAsync(path));
        var stored = Services.PreviewStore.Retrieve(preview.PreviewToken);
        Assert.IsNotNull(stored);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(stored.Value.ModifiedSolution, "SampleLib.SignatureProof"));
        Assert.AreEqual(2, preview.CallsiteUpdates!.Sum(update => update.CallsiteCount));
        var applied = await Services.RefactoringService.ApplyRefactoringAsync(preview.PreviewToken, "test_apply", CancellationToken.None);
        Assert.IsTrue(applied.Success, applied.Error);
        Assert.AreEqual("0|3", await RunSignatureFixtureAsync(Services.WorkspaceManager.GetCurrentSolution(workspace.WorkspaceId), "SampleLib.SignatureProof"));
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_JsonSerializerDefaultCreation()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static string Run() => new SelectedStruct(2).Value + \"|\" + System.Text.Json.JsonSerializer.Deserialize<SelectedStruct>(\"{}\").Value; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedInitializerRetainsEffects()
    {
        var declaration = "public struct SelectedStruct(int unused) : IChild { public int Value = 7; public Child Child => SignatureProof.GetChild(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IChild { Child Child { get; } } public class Child { public int A; public int B; }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Child child = new(); public static Child GetChild() { trace += \"H\"; return child; } static int Trace(int value) { trace += value; return value; } public static T Zero<T>() where T : IChild, new() => new T { Child = { A = Trace(3), B = Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = Zero<SelectedStruct>(); return selected.Value + \"|\" + zero.Value + \"|\" + child.A + \"|\" + child.B + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_DiscardedZeroConstruction(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic static class SignatureProof { public static void Zero<T>() where T : new() { new T(); } public static int Run() { "
            + (generic ? "Zero<SelectedStruct>();" : "new SelectedStruct();") + " return new SelectedStruct(2).Value; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7", 1);
    }

    [TestMethod]
    [DataRow("arrow")]
    [DataRow("constructor")]
    [DataRow("for")]
    public async Task ChangeSignaturePreview_LastStructParameter_DiscardedConstructionHosts(string host)
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var member = host switch
        {
            "arrow" => "public static void Zero<T>() where T : new() => new T();",
            "constructor" => "public class Factory<T> where T : new() { public Factory() => new T(); }",
            _ => "public static void Zero<T>() where T : new() { for (new T(), Trace(1); count < 2; new T(), Trace(2)) { count++; try { if (count == 1) continue; trace += \"B\"; } finally { trace += \"F\"; } } }",
        };
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { static int count; static string trace = \"\"; static int Trace(int value) { trace += value; return value; } "
            + member + " public static string Run() { " + (host == "constructor" ? "new Factory<SelectedStruct>();" : "Zero<SelectedStruct>();")
            + " return new SelectedStruct(2).Value + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, host == "for" ? "7|1F2BF2" : "7|", 1);
    }

    [TestMethod]
    public async Task ChangeSignaturePreview_LastStructParameter_ExpressionTreeDefaultConstruction()
    {
        var declaration = "public struct SelectedStruct(int unused) { public int Value = 7; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic class OtherClass { public int Value = 11; } public static class SignatureProof { public static System.Linq.Expressions.Expression<System.Func<T>> Zero<T>() where T : new() => () => new T(); public static long Allocated<T>(System.Func<T> factory) { for (int i = 0; i < 10000; i++) _ = factory(); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) _ = factory(); return System.GC.GetAllocatedBytesForCurrentThread() - before; } public static string Run() { var factory = Zero<SelectedStruct>().Compile(); return new SelectedStruct(2).Value + \"|\" + factory().Value + \"|\" + Zero<OtherClass>().Compile()().Value + \"|\" + Allocated(factory); } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|11|0", 1);
    }

    [TestMethod]
    [DataRow("add")]
    [DataRow("remove")]
    [DataRow("last")]
    public async Task ChangeSignaturePreview_SelectedStructDefaultConstructor_RetainsIdentity(string operation)
    {
        var declaration = operation == "last"
            ? "public struct SelectedStruct(int unused) { public int Value => 0; }"
            : "public struct SelectedStruct(int first, int unused) { public int Value => first; }";
        var source = "namespace SampleLib;\n" + declaration + "\npublic static class SignatureProof { public static int Run() => new SelectedStruct().Value * 100 + "
            + (operation == "last" ? "new SelectedStruct(2)" : "new SelectedStruct(1, 2)") + ".Value; }";
        var request = operation == "add" ? new ChangeSignatureRequest("add", Position: 1, Name: "added", ParameterType: "int")
            : new ChangeSignatureRequest("remove", Name: "unused");
        await AssertSignatureProofAsync(source, request, 2, declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1,
            operation == "last" ? "0" : "1", 1);
    }
}
