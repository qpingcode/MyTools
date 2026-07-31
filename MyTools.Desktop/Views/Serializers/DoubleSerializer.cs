namespace MyTools.Desktop.Serializers;

public class DoubleSerializer: IRegistrySerializer<double>
{
    public double DeserializeT(string value)
    {
        if (double.TryParse(value, out var result))
        {
            return result;
        }
        throw new NotSupportedException("Invalid double string: {value}");
    }

    public string SerializeT(double value)
    {
        return value.ToString();
    }

    public Type SerializedType => typeof(double);
}