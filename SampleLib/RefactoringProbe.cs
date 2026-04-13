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
}
