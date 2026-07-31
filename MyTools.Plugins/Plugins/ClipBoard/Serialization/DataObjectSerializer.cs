using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using MyTools.Common;
using MyTools.Plugins.Serialization;

namespace MyTools.Plugins;

public class DataObjectSerializer
{
    public const string MyToolsNotSaveHisotryFormat = "MyTools.DataObject.NotSaveHistory";
    public static byte[] SerializeIDataObject()
    {
        var serializable = new Dictionary<string, DataEntry>();

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            serializable[DataFormats.UnicodeText] = new DataEntry(SerializeDataType.String, text);
        }

        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image != null)
            {
                using var ms = new MemoryStream();
                var encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(ms);
                
                serializable[DataFormats.Bitmap] = new DataEntry(SerializeDataType.ByteArray, ms.ToArray());
            }
        }

        if (Clipboard.ContainsFileDropList())
        {
            var fileDropList = Clipboard.GetFileDropList();
            serializable[DataFormats.FileDrop] = new DataEntry(SerializeDataType.StringArray, fileDropList);
        }

        if (Clipboard.ContainsData(DataFormats.Html))
        {
            var cfHtml = Clipboard.GetText(TextDataFormat.Html);
            serializable[DataFormats.Html] = new DataEntry(SerializeDataType.String, cfHtml);
        }

        if (Clipboard.ContainsData(DataFormats.Rtf))
        {
            var cfRtf = Clipboard.GetText(TextDataFormat.Rtf);
            serializable[DataFormats.Rtf] = new DataEntry(SerializeDataType.String, cfRtf);
        }

        return CustomMapSerializer.Serialize(serializable);
    }
    
    public static byte[] SerializeIDataObject(IDataObject dataObject)
    {
        var serializable = new Dictionary<string, DataEntry>();

        var dataFormats = dataObject.GetFormats();

        foreach (var dataFormat in dataFormats)
        {
            if (dataFormat == DataFormats.Bitmap)
            {
                var image = dataObject.GetData(dataFormat) as BitmapSource;
                if (image == null)
                {
                    continue;
                }
                using var ms = new MemoryStream();
                var encoder = new BmpBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(ms);
                
                serializable[DataFormats.Bitmap] = new DataEntry(SerializeDataType.ByteArray, ms.ToArray());
            }
            else if (dataFormat == DataFormats.FileDrop)
            {
                var fileDropData = dataObject.GetData(DataFormats.FileDrop);
                StringCollection fileDropList;
                if (fileDropData is StringCollection collection)
                {
                    fileDropList = collection;
                }
                else if (fileDropData is string[] fileArray)
                {
                    fileDropList = new StringCollection();
                    fileDropList.AddRange(fileArray);
                }
                else
                {
                    continue;
                }
                serializable[DataFormats.FileDrop] = new DataEntry(SerializeDataType.StringArray, fileDropList);
            }
            else
            {
                serializable[dataFormat] = new DataEntry(SerializeDataType.String, dataObject.GetData(dataFormat));
            }

        }

        return CustomMapSerializer.Serialize(serializable);
    }

    public static (DataObject dataObject, PreviewContentType previewContentType, byte[] previewContent) DeserializeToIDataObject(byte[] data)
    {
        var dic = CustomMapSerializer.Deserialize(data);
        var dataObject = new DataObject();
        PreviewContentType previewContentType = PreviewContentType.Text;
        byte[] previewContent = [];
        foreach (var entry in dic)
        {
            SetPreivewContentIfNull(entry, ref previewContentType, ref previewContent);
            var value = entry.Value.Value;

            if (entry.Key == DataFormats.Bitmap)
            {
                using var ms = new MemoryStream(entry.Value.Value as byte[] ?? Array.Empty<byte>());
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                dataObject.SetData(entry.Key, bitmap);
            }
            else if (entry.Key == DataFormats.FileDrop)
            {
                var fileDropList = value as StringCollection ?? new StringCollection();
                dataObject.SetFileDropList(fileDropList);
            }
            else
            {
                dataObject.SetData(entry.Key, value);
            }
        }
        
        dataObject.SetData(MyToolsNotSaveHisotryFormat, "true");
        return (dataObject, previewContentType, previewContent);
    }

    private static void SetPreivewContentIfNull(KeyValuePair<string, DataEntry> entry, ref PreviewContentType previewContentType, ref byte[] previewContent)
    {
        if (previewContent.Length != 0)
            return;

        var key = entry.Key;
        var type = entry.Value.Type;
        var value = entry.Value.Value;
        
        if (type == SerializeDataType.String)
        {
            previewContent = Encoding.UTF8.GetBytes(entry.Value.Value as string ?? string.Empty);
            previewContentType = PreviewContentType.Text;
        }
        else if (type == SerializeDataType.ByteArray && key == DataFormats.Bitmap)
        {
            previewContent = entry.Value.Value as byte[] ?? Array.Empty<byte>();
            previewContentType = PreviewContentType.Image;
        }
        else if (type == SerializeDataType.StringArray && key == DataFormats.FileDrop)
        {
            var stringCollection = entry.Value.Value as StringCollection ?? new StringCollection();
            var stringlist = string.Join("\r\n", stringCollection.Cast<string>());
            previewContent = Encoding.UTF8.GetBytes(stringlist);
            previewContentType = PreviewContentType.Text;
        }
    }
}