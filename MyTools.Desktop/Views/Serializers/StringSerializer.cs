namespace MyTools.Desktop.Serializers;

public class StringSerializer: IRegistrySerializer<string>
{
    public string DeserializeT(string value)
    {
        return value;
    }

    public string SerializeT(string value)
    {
        return value;
    }

    public Type SerializedType => typeof(string);
}