using System.IO;
using System.Text;
using System.Collections.Specialized;

namespace MyTools.Plugins.Serialization;

public class CustomMapSerializer
{
    public static byte[] Serialize(IDictionary<string, DataEntry> dataMap)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
        bw.Write(dataMap.Count);
        foreach (var kv in dataMap)
        {
            bw.Write(kv.Key);
            bw.Write((int)kv.Value.Type);
            switch (kv.Value.Type)
            {
                case SerializeDataType.String:
                    bw.Write((string)kv.Value.Value);
                    break;
                case SerializeDataType.ByteArray:
                    var bytes = (byte[])kv.Value.Value;
                    bw.Write(bytes.Length);
                    bw.Write(bytes);
                    break;
                case SerializeDataType.StringArray:
                    var collection = (StringCollection)kv.Value.Value;
                    bw.Write(collection.Count);
                    foreach (var s in collection)
                    {
                        bw.Write(s ?? string.Empty);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported SerializeDataType: {kv.Value.Type}");
            }
        }
        return ms.ToArray();
    }
    
    public static IDictionary<string, DataEntry> Deserialize(byte[] serializedData)
    {
        var dict = new Dictionary<string, DataEntry>();
        using var ms = new MemoryStream(serializedData);
        using var br = new BinaryReader(ms, Encoding.UTF8, true);
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string key = br.ReadString();
            SerializeDataType type = (SerializeDataType)br.ReadInt32();
            object value;
            switch (type)
            {
                case SerializeDataType.String:
                    value = br.ReadString();
                    break;
                case SerializeDataType.ByteArray:
                    int len = br.ReadInt32();
                    value = br.ReadBytes(len);
                    break;
                case SerializeDataType.StringArray:
                    int arrLen = br.ReadInt32();
                    var sc = new StringCollection();
                    for (int j = 0; j < arrLen; j++)
                    {
                        sc.Add(br.ReadString());
                    }
                    value = sc;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported SerializeDataType: {type}");
            }
            dict[key] = new DataEntry(type, value);
        }
        return dict;
    }
}

public class DataEntry
{
    public DataEntry(SerializeDataType type, object value)
    {
        Type = type;
        Value = value;
    }
    public SerializeDataType Type { get; }
    public object Value { get; }
}

public enum SerializeDataType
{
    String,
    ByteArray,
    StringArray
}
