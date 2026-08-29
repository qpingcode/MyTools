namespace MyTools.Common.Config.Enums;

/// <summary>Value type of one column / editor field in an array setting schema.</summary>
public enum SchemaPropertyType
{
    String,
    Bool,
    Int,
    Double,
    Path,
    Hidden
}

public static class SchemaPropertyTypeExtensions
{
    /// <summary>Wire representation consumed by the settings web UI.</summary>
    public static string ToWireString(this SchemaPropertyType type) => type switch
    {
        SchemaPropertyType.String => "string",
        SchemaPropertyType.Bool => "bool",
        SchemaPropertyType.Int => "int",
        SchemaPropertyType.Double => "double",
        SchemaPropertyType.Path => "path",
        SchemaPropertyType.Hidden => "hidden",
        _ => "string"
    };
}
