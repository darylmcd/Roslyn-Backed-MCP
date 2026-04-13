namespace SampleLib.Hierarchy;

public abstract class Shape
{
    public abstract double Area();

    public virtual string Describe() => $"{GetType().Name} with area {Area():F2}";
}
