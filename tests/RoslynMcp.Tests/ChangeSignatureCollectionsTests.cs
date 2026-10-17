using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Contracts;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ChangeSignatureCollectionsTests : ChangeSignatureTestBase
{

    [TestMethod]
    public async Task ChangeSignaturePreview_ParamsCollection_PreservesBuilderAllocationAndEvaluationOrder()
    {
        var declaration = "public class BuilderTarget(int first, params NumberCollection tail) { public int Sum => first + System.Linq.Enumerable.Sum(tail); }";
        var source = "namespace SampleLib;\n" + declaration + "\n" +
            "[System.Runtime.CompilerServices.CollectionBuilder(typeof(NumberBuilder), \"Create\")] " +
            "public class NumberCollection : System.Collections.Generic.IEnumerable<int> { private readonly int[] items; " +
            "public NumberCollection(int[] source) { SignatureProof.TraceText += \"A\"; items = source; } " +
            "public System.Collections.Generic.IEnumerator<int> GetEnumerator() => ((System.Collections.Generic.IEnumerable<int>)items).GetEnumerator(); " +
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); } " +
            "public static class NumberBuilder { public static NumberCollection Create(System.ReadOnlySpan<int> items) " +
            "{ SignatureProof.TraceText += \"B\"; return new NumberCollection(items.ToArray()); } } " +
            "public static class SignatureProof { public static string TraceText = \"\"; " +
            "public static int Trace(int value) { TraceText += value; return value; } " +
            "public static string Run() { var value = new BuilderTarget(Trace(1), Trace(2), Trace(3)); return TraceText + \"|\" + value.Sum; } }";
        await AssertSignatureProofAsync(source,
            new ChangeSignatureRequest("add", Name: "marker", ParameterType: "int", Position: 0),
            2, declaration.IndexOf("BuilderTarget", StringComparison.Ordinal) + 1, "123BA|6", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionInitializerRetainsEffects(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } public static "
            + (generic ? "T Zero<T>() where T : IBag, new()" : "SelectedStruct Zero()")
            + " => new " + (generic ? "T" : "SelectedStruct") + " { /*signature-dot*/ Trace(3), Trace(4) }; public static string Run() { var selected = new SelectedStruct(2); var zero = "
            + (generic ? "Zero<SelectedStruct>()" : "Zero()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + zero.Sum + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|7|34", 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_NestedCollectionRetainsEffects(bool generic)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBagOwner { public int Value = 7; public Bag Bag => SignatureProof.GetBag(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBagOwner { Bag Bag { get; } } public class Bag : System.Collections.Generic.List<int> { public new void Add(int value) { SignatureProof.Mark(); base.Add(value); } }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static Bag bag = new(); public static Bag GetBag() { trace += \"H\"; return bag; } public static void Mark() { trace += \"A\"; } static int Trace(int value) { trace += value; return value; } public static "
            + (generic ? "T Zero<T>() where T : IBagOwner, new()" : "SelectedStruct Zero()") + " => new " + (generic ? "T" : "SelectedStruct")
            + " { Bag = { /*signature-dot*/ Trace(3), Trace(4) } }; public static string Run() { var selected = new SelectedStruct(2); var zero = "
            + (generic ? "Zero<SelectedStruct>()" : "Zero()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + string.Join(\"\", bag) + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("static")]
    [DataRow("receiver")]
    [DataRow("ref")]
    [DataRow("readonly-receiver")]
    [DataRow("generic-receiver")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionArgumentRetainsEffects(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } " + (shape == "receiver"
                ? "public struct Receiver { public int Calls; public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Zero<T>() where T : IBag, new() { var receiver = new Receiver(); var result = receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + receiver.Calls; }"
                : shape == "readonly-receiver"
                    ? "public struct Receiver { public int Calls; public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Call<T>(in Receiver receiver) where T : IBag, new() => receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); public static string Zero<T>() where T : IBag, new() { var receiver = new Receiver(); var result = Call<T>(in receiver); return result + \"|\" + receiver.Calls; }"
                : shape == "generic-receiver"
                    ? "public interface IReceiver { int Calls { get; } string Take<T>(int before, T value, int after) where T : IBag; } public struct Receiver : IReceiver { public int Calls { get; private set; } public string Take<T>(int before, T value, int after) where T : IBag { Calls++; return value.Sum + \"|\" + trace; } } public static string Call<T, R>() where T : IBag, new() where R : IReceiver, new() { var receiver = new R(); var result = receiver.Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + receiver.Calls; } public static string Zero<T>() where T : IBag, new() => Call<T, Receiver>();"
                : shape == "ref"
                    ? "public static string Take<T>(ref int before, T value, int after) where T : IBag { before = 9; return value.Sum + \"|\" + trace; } public static string Zero<T>() where T : IBag, new() { int[] before = { 1 }; var result = Take(ref before[Trace(0)], new T { Trace(3), Trace(4) }, Trace(5)); return result + \"|\" + before[0]; }"
                    : "public static string Take<T>(int before, T value, int after) where T : IBag => value.Sum + \"|\" + trace; public static string Zero<T>() where T : IBag, new() => Take(Trace(1), new T { Trace(3), Trace(4) }, Trace(5));")
            + " public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, shape is "receiver" or "generic-receiver" ? "7|7|1345|1" : shape == "readonly-receiver" ? "7|7|1345|0" : shape == "ref" ? "7|7|0345|9" : "7|7|1345", 1);
    }

    [TestMethod]
    [DataRow("void")]
    [DataRow("binary")]
    [DataRow("conditional-true")]
    [DataRow("conditional-false")]
    [DataRow("and-true")]
    [DataRow("and-false")]
    [DataRow("coalesce-null")]
    [DataRow("coalesce-value")]
    [DataRow("if")]
    [DataRow("while")]
    [DataRow("for")]
    [DataRow("do")]
    [DataRow("foreach")]
    [DataRow("using")]
    [DataRow("lambda")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionControlFlow(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public void Dispose() { SignatureProof.Mark(); } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var collection = "new T { Trace(3), Trace(4) }";
        var member = shape switch
        {
            "void" => "public static void Zero<T>() where T : IBag, new() { Consume(Trace(1), " + collection + ", Trace(5)); }",
            "binary" => "public static string Zero<T>() where T : IBag, new() => Left() + Take(" + collection + ");",
            "conditional-true" or "conditional-false" => "public static string Zero<T>() where T : IBag, new() => Gate(" + (shape.EndsWith("true", StringComparison.Ordinal) ? "true" : "false") + ") ? Take(" + collection + ") : Take(new T { Trace(8) });",
            "and-true" or "and-false" => "public static bool Zero<T>() where T : IBag, new() => Gate(" + (shape.EndsWith("true", StringComparison.Ordinal) ? "true" : "false") + ") && Test(" + collection + ");",
            "coalesce-null" or "coalesce-value" => "public static string Zero<T>() where T : IBag, new() => Maybe(" + (shape.EndsWith("null", StringComparison.Ordinal) ? "true" : "false") + ") ?? Take(" + collection + ");",
            "if" => "public static string Zero<T>() where T : IBag, new() { if (Test(" + collection + ")) trace += \"I\"; return trace; }",
            "for" => "public static string Zero<T>() where T : IBag, new() { for (; Test(" + collection + "); Trace(6)) { if (count == 1) continue; break; } return trace; }",
            "do" => "public static string Zero<T>() where T : IBag, new() { do { trace += \"W\"; if (count == 0) { count++; continue; } break; } while (Test(" + collection + ")); return trace; }",
            "foreach" => "public static string Zero<T>() where T : IBag, new() { foreach (var item in " + collection + ") trace += item; return trace; }",
            "using" => "public static string Zero<T>() where T : IBag, new() { using (" + collection + ") trace += \"U\"; return trace; }",
            "lambda" => "public static string Zero<T>() where T : IBag, new() { System.Action action = () => Consume(Trace(1), " + collection + ", Trace(5)); action(); return trace; }",
            _ => "public static string Zero<T>() where T : IBag, new() { while (Test(" + collection + ")) { trace += \"W\"; if (count == 1) continue; break; } return trace; }",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int>, System.IDisposable { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int count; public static void Mark() { trace += \"D\"; } static int Trace(int value) { trace += value; return value; } static bool Gate(bool value) { trace += \"G\"; return value; } static string Left() { trace += \"L\"; return \"left\"; } static string? Maybe(bool isNull) { trace += \"M\"; return isNull ? null : \"value\"; } static bool Test<T>(T value) where T : IBag { count++; trace += \"T\"; return value.Sum == 7; } static string Take<T>(T value) where T : IBag => value.Sum.ToString(); static void Consume<T>(int before, T value, int after) where T : IBag { trace += \"B\"; } "
            + member + " public static string Run() { " + (shape == "void" ? "Zero<SelectedStruct>(); var value = trace;" : "var value = Zero<SelectedStruct>();")
            + " return new SelectedStruct(2).Value + \"|\" + value + \"|\" + trace; } }";
        source = source.Replace(" ? Take(", " ? /*signature-dot*/ Take(", StringComparison.Ordinal)
            .Replace(" && Test(", " && /*signature-dot*/ Test(", StringComparison.Ordinal)
            .Replace(" ?? Take(", " ?? /*signature-dot*/ Take(", StringComparison.Ordinal);
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("while")]
    [DataRow("for")]
    [DataRow("do")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomTruthLoopRetainsEffects(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var condition = "Test(new T { Trace(3), Trace(4) })";
        var loop = shape == "for" ? "for (; " + condition + "; Trace(6)) { trace += \"W\"; continue; }"
            : shape == "do" ? "do { trace += \"W\"; continue; } while (" + condition + ");"
            : "while (" + condition + ") { trace += \"W\"; continue; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int count; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return value.Value; } public static bool operator false(Truth value) { trace += \"F\"; return false; } } static int Trace(int value) { trace += value; return value; } static Truth Test<T>(T value) where T : IBag { count++; trace += \"C\"; return new Truth(count < 2); } public static string Zero<T>() where T : IBag, new() { "
            + loop + " return trace; } public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(); }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomShortCircuitInlineAssignment(bool value)
    {
        var declaration = "public struct SelectedStruct(int unused) : IValue { public int Value = 7; public int Other { get; set; } }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IValue { int Other { get; set; } } public static class SignatureProof { static string trace = \"\"; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return true; } public static bool operator false(Truth value) { trace += \"F\"; return !value.Value; } public static Truth operator &(Truth left, Truth right) { trace += \"A\"; return new Truth(left.Value && right.Value); } } static int Trace() { trace += \"3\"; return 3; } static Truth Test<T>(T value) where T : IValue => new Truth(value.Other == 3); public static Truth Zero<T>(Truth left) where T : IValue, new() => left && Test(new T { Other = Trace() }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(new Truth("
            + (value ? "true" : "false") + ")).Value + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, value ? "7|True|F3A" : "7|False|F", 1);
    }

    [TestMethod]
    [DataRow("and", false)]
    [DataRow("and", true)]
    [DataRow("or", false)]
    [DataRow("or", true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomShortCircuitCollection(string shape, bool value)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) { trace += \"T\"; return value.Value; } public static bool operator false(Truth value) { trace += \"F\"; return !value.Value; } public static Truth operator &(Truth left, Truth right) { trace += \"A\"; return new Truth(left.Value && right.Value); } public static Truth operator |(Truth left, Truth right) { trace += \"O\"; return new Truth(left.Value || right.Value); } } static int Trace(int value) { trace += value; return value; } static Truth Test<T>(T bag, [System.Runtime.CompilerServices.CallerMemberName] string caller = \"\") where T : IBag { trace += caller; return new Truth(bag.Sum == 7); } public static Truth Zero<T>(Truth left) where T : IBag, new() => left "
            + (shape == "and" ? "&&" : "||") + " /*signature-dot*/ Test(new T { Trace(3), Trace(4) }); public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>(new Truth("
            + (value ? "true" : "false") + ")).Value + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionRefCapture(bool deferred)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static int Trace(ref int value, [System.Runtime.CompilerServices.CallerMemberName] string caller = \"\") { if (caller != \"Zero\") throw new System.InvalidOperationException(caller); return ++value; } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); public static Truth Zero<T>(ref int count) where T : IBag, new() => new Truth(" + (deferred ? "true" : "false") + ") && Test(new T { Trace(ref count), Trace(ref count) }); public static string Run() { int count = 0; Zero<SelectedStruct>(ref count); var before = System.GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) Zero<SelectedStruct>(ref count); var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before; return new SelectedStruct(2).Value + \"|\" + count + \"|\" + allocated; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, deferred ? "7|202|0" : "7|0|0", 1);
    }

    [TestMethod]
    [DataRow("await")]
    [DataRow("span")]
    [DataRow("instance")]
    [DataRow("readonly")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionOwnership(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var member = shape == "await"
            ? "static System.Threading.Tasks.Task<Truth> TestAsync<T>(T bag) where T : IBag => System.Threading.Tasks.Task.FromResult(Test(bag)); public static async System.Threading.Tasks.Task<Truth> Zero<T>() where T : IBag, new() => new Truth(true) && await TestAsync(new T { 3, 4 });"
            : shape == "span" ? "public static Truth Zero<T>() where T : IBag, new() { System.Span<int> values = stackalloc int[] { 3, 4 }; return new Truth(true) && Test(new T { values[0], values[1] }); }"
            : "public struct Factory { public int Count; int Trace() => ++Count; public " + (shape == "readonly" ? "readonly " : "") + "Truth Zero<T>() where T : IBag, new() => new Truth(true) && Test(new T { Trace(), Trace() }); }";
        var call = shape == "await" ? "Zero<SelectedStruct>().GetAwaiter().GetResult().Value"
            : shape == "span" ? "Zero<SelectedStruct>().Value" : "factory.Zero<SelectedStruct>().Value";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); "
            + member + " public static string Run() { " + (shape is "instance" or "readonly" ? "var factory = new Factory();" : "")
            + " return new SelectedStruct(2).Value + \"|\" + " + call + (shape is "instance" or "readonly" ? " + \"|\" + factory.Count" : "") + "; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, shape == "instance" ? "7|True|2" : shape == "readonly" ? "7|True|0" : "7|True", 1);
    }

    [TestMethod]
    [DataRow("checked")]
    [DataRow("unchecked")]
    [DataRow("constructor")]
    [DataRow("delegate")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionExpressionHosts(string shape)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var expression = shape == "constructor" ? "new Box(Trace(1), new T { Trace(3), Trace(4) }, Trace(5)).Value"
            : shape == "delegate" ? "callee(Trace(1), new T { Trace(3), Trace(4) }, Trace(5))"
            : shape + "(Take(new T { Trace(int.MaxValue) + Trace(1) }))";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value + \",\"; return value; } static int Take<T>(T bag) where T : IBag => bag.Sum; public sealed class Box { public int Value; public Box(int first, IBag bag, int last) { Value = bag.Sum; } } public static int Zero<T>() where T : IBag, new() { System.Func<int,T,int,int> callee = (first, bag, last) => bag.Sum; try { return "
            + expression + "; } catch (System.OverflowException) { return 9; } } public static string Run() => new SelectedStruct(2).Value + \"|\" + Zero<SelectedStruct>() + \"|\" + trace; }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, null, 1);
    }

    [TestMethod]
    [DataRow("out")]
    [DataRow("assigned-out")]
    [DataRow("in")]
    public async Task ChangeSignaturePreview_LastStructParameter_CustomCollectionParameterKinds(string kind)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var member = kind == "in"
            ? "static int Fill(in int count) => count; public static Truth Zero<T>(in int count) where T : IBag, new() => new Truth(true) && Test(new T { Fill(in count), Fill(in count) });"
            : "static int Fill(out int count) { count = 3; return count; } public static Truth Zero<T>(out int count) where T : IBag, new() { " + (kind == "assigned-out" ? "count = 0;" : "") + " var value = new Truth(true) && Test(new T { Fill(out count), Fill(out count) }); count = 9; return value; }";
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { public struct Truth(bool value) { public bool Value => value; public static bool operator true(Truth value) => value.Value; public static bool operator false(Truth value) => !value.Value; public static Truth operator &(Truth left, Truth right) => new Truth(left.Value && right.Value); } static Truth Test<T>(T bag) where T : IBag => new Truth(bag.Sum > 0); "
            + member + " public static string Run() { int count = 3; var value = Zero<SelectedStruct>(" + (kind == "in" ? "in" : "out") + " count); return new SelectedStruct(2).Value + \"|\" + value.Value + \"|\" + count; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, kind == "in" ? "7|True|3" : "7|True|9", 1);
    }

    [TestMethod]
    [DataRow("property")]
    [DataRow("getter")]
    [DataRow("lambda")]
    [DataRow("local")]
    public async Task ChangeSignaturePreview_LastStructParameter_CollectionInitializerHosts(string host)
    {
        var declaration = "public struct SelectedStruct(int unused) : IBag { public int Value = 7; public int Sum { get; private set; } public void Add(int value) { Sum += value; } public System.Collections.Generic.IEnumerator<int> GetEnumerator() => System.Linq.Enumerable.Empty<int>().GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); }";
        var factory = host switch
        {
            "property" => "public static SelectedStruct Zero => new SelectedStruct { Trace(3), Trace(4) };",
            "getter" => "public static SelectedStruct Zero { get => new SelectedStruct { Trace(3), Trace(4) }; }",
            "lambda" => "public static System.Func<SelectedStruct> Zero = () => new SelectedStruct { Trace(3), Trace(4) };",
            _ => "public static SelectedStruct Zero() { SelectedStruct Local() => new SelectedStruct { Trace(3), Trace(4) }; return Local(); }",
        };
        var source = "namespace SampleLib;\n" + declaration
            + "\npublic interface IBag : System.Collections.Generic.IEnumerable<int> { int Sum { get; } void Add(int value); }"
            + "\npublic static class SignatureProof { static string trace = \"\"; static int Trace(int value) { trace += value; return value; } "
            + factory + " public static string Run() { var selected = new SelectedStruct(2); var zero = Zero"
            + (host is "property" or "getter" ? "" : "()") + "; return selected.Value + \"|\" + zero.Value + \"|\" + zero.Sum + \"|\" + trace; } }";
        await AssertSignatureProofAsync(source, new ChangeSignatureRequest("remove", Name: "unused"), 2,
            declaration.IndexOf("SelectedStruct", StringComparison.Ordinal) + 1, "7|0|7|34", 1);
    }
}
