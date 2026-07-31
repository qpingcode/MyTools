using System.Windows;
using MyTools.Common;

namespace MyTools.Plugins.Param;

public class ClipboardParam(IDataObject dataObject) : IClipboardSource, IActionParams
{
    object IClipboardSource.GetDataForClipboard()
    {
        return dataObject;
    }
}