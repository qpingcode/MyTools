using System.Collections.Specialized;
using System.IO;
using System.Windows;
using MyTools.Common;
using MyTools.Plugins.Param;

namespace MyTools.Desktop.Components;

public static class ResultFileDragSource
{
    public static DataObject? CreateDataObject(IActionParams args)
    {
        IEnumerable<string> paths = args switch
        {
            ActionStringParam path => new[] { path.GetValue() },
            IClipboardSource clipboard => ReadFilePaths(clipboard.GetDataForClipboard()),
            _ => []
        };
        var existingPaths = paths
            .Where(path => Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existingPaths.Length == 0) return null;

        var files = new StringCollection();
        files.AddRange(existingPaths);
        var data = new DataObject();
        data.SetFileDropList(files);
        return data;
    }

    private static IEnumerable<string> ReadFilePaths(object value)
    {
        if (value is not IDataObject data || !data.GetDataPresent(DataFormats.FileDrop)) return [];
        return data.GetData(DataFormats.FileDrop) switch
        {
            string[] paths => paths,
            StringCollection paths => paths.Cast<string>(),
            _ => []
        };
    }
}
