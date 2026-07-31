namespace MyTools.Desktop.Serializers;

public class BoolSerializer: IRegistrySerializer<bool>
{
    const string TrueString = "True";
    const string FalseString = "False";

    public bool DeserializeT(string value)
    {
        if (value == TrueString) return true;
        if (value == FalseString) return false;
        throw new NotSupportedException($"Invalid boolean string: {value}");
    }

    public string SerializeT(bool value)
    {
        return value ? TrueString : FalseString;
    }

    public Type SerializedType => typeof(bool);
}