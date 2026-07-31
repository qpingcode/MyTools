using MyTools.Common.Config.Interfaces;

namespace MyTools.Desktop.Serializers;

public interface IRegistrySerializer<T> : IRegistrySerializer
{
    object? IRegistrySerializer.Deserialize(string value)
    {
        return DeserializeT(value);
    }

    string IRegistrySerializer.Serialize(object value)
    {
        if (value is T tValue)
        {
            return SerializeT(tValue);
        }
        throw new ArgumentException($"Cannot serialize {value.GetType()}");
    }

    public T? DeserializeT(string value);

    public string SerializeT(T value);
}