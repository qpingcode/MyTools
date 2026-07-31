namespace MyTools.Desktop.Serializers;

public class IntegerSerializer: IRegistrySerializer<int>
{
    public int DeserializeT(string value)
    {
        if (int.TryParse(value, out var result))
        {
            return result;
        }
        throw new NotSupportedException("Invalid integer string: {value}");
    }

    public string SerializeT(int value)
    {
        return value.ToString();
    }

    public Type SerializedType => typeof(int);
}