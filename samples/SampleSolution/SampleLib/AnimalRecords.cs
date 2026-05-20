namespace SampleLib;

/// <summary>
/// Positional record class. Used by SymbolMapperTests to verify <c>kind == "Record"</c>
/// (regression cover for the historical bug where <see cref="INamedTypeSymbol.TypeKind"/>
/// returned <c>"Class"</c> for record classes).
/// </summary>
public record AnimalRecord(string Name, int Legs);

/// <summary>
/// Record struct. Used by SymbolMapperTests to verify <c>kind == "RecordStruct"</c>.
/// </summary>
public readonly record struct AnimalCoord(double Latitude, double Longitude);
