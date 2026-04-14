namespace SampleLib;

/// <summary>
/// Fixture class for testing selection-range code actions (extract method,
/// introduce variable, inline variable). Each method contains patterns
/// that should trigger specific refactoring providers.
/// </summary>
public class RefactoringProbe
{
    // Lines 11-16: extractable block (3 statements)
    public int ComputeAndPrint(int a, int b)
    {
        var sum = a + b;
        var doubled = sum * 2;
        Console.WriteLine(doubled);
        return doubled;
    }

    // Line 21: expression suitable for "introduce variable"
    public double CalculateArea(double radius)
    {
        return Math.PI * radius * radius;
    }

    // Lines 26-29: variable suitable for "inline"
    public string FormatGreeting(string name)
    {
        var greeting = $"Hello, {name}!";
        return greeting;
    }

    // Lines 35-39: extract a region that REASSIGNS an existing local. The previous extract
    // method implementation always emitted `var result = M(...)` at the call site, producing
    // CS0136+CS0841. Post-fix it must emit `result = M(...)` (plain assignment) so the
    // extracted method can be applied without breaking compilation.
    public int ReassignedLocalScenario(int input)
    {
        int result = input;
        result = result * 2;
        result = result + 5;
        return result;
    }
}
