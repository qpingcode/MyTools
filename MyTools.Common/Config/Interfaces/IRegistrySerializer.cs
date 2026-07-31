namespace MyTools.Common.Config.Interfaces;

public interface IRegistrySerializer
{
    object? Deserialize(string value);
    string Serialize(object value);
    Type SerializedType { get; }
}


