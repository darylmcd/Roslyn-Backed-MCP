namespace SampleLib.Hierarchy;

public class Rectangle : Shape
{
    public double Width { get; }
    public double Height { get; }

    public Rectangle(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public override double Area() => Width * Height;

    public override string Describe() => $"Rectangle {Width}x{Height} with area {Area():F2}";
}
