using System.Windows;
using System.Collections.Specialized;
using MyTools.Common;

namespace MyTools.Plugins.Param;

/// <summary>
/// 延迟加载的 ClipboardParam，只有在 GetDataForClipboard 被调用时才从数据库加载并反序列化内容
/// </summary>
public class LazyClipboardParam : IClipboardSource, IActionParams, IPreviewContentProvider
{
    private readonly ClipBoardDbHelper _dbHelper;
    private readonly int _itemId;
    private IDataObject? _cachedDataObject;
    private (PreviewContentType previewContentType, byte[] previewContent)? _cachedPreview;

    public LazyClipboardParam(ClipBoardDbHelper dbHelper, int itemId)
    {
        _dbHelper = dbHelper;
        _itemId = itemId;
    }

    public string? GetPlainText()
    {
        var dataObject = LoadDataObject();
        if (dataObject.GetDataPresent(DataFormats.UnicodeText, true))
        {
            return dataObject.GetData(DataFormats.UnicodeText, true) as string;
        }

        if (dataObject.GetDataPresent(DataFormats.Text, true))
        {
            return dataObject.GetData(DataFormats.Text, true) as string;
        }

        var files = dataObject.GetData(DataFormats.FileDrop, true);
        return files switch
        {
            string[] paths => string.Join(Environment.NewLine, paths),
            StringCollection paths => string.Join(Environment.NewLine, paths.Cast<string>()),
            _ => null
        };
    }

    object IClipboardSource.GetDataForClipboard()
    {
        return LoadDataObject();
    }

    private IDataObject LoadDataObject()
    {
        if (_cachedDataObject != null)
        {
            return _cachedDataObject;
        }

        var content = _dbHelper.GetContentById(_itemId);
        if (content == null)
        {
            throw new InvalidOperationException($"Clipboard history item with id {_itemId} not found");
        }

        var (dataObject, previewContentType, previewContent) = DataObjectSerializer.DeserializeToIDataObject(content);
        _cachedDataObject = dataObject;
        _cachedPreview = (previewContentType, previewContent);
        return dataObject;
    }

    /// <summary>
    /// 获取预览内容，用于在 WPF 中显示预览。如果已经缓存了完整数据，则使用缓存；否则只加载预览所需的部分。
    /// </summary>
    (PreviewContentType previewContentType, byte[] previewContent) IPreviewContentProvider.GetPreviewContent()
    {
        if (_cachedPreview.HasValue)
        {
            return _cachedPreview.Value;
        }

        var content = _dbHelper.GetContentById(_itemId);
        if (content == null)
        {
            return (PreviewContentType.Text, []);
        }

        var (dataObject, previewContentType, previewContent) = DataObjectSerializer.DeserializeToIDataObject(content);
        _cachedDataObject = dataObject;
        _cachedPreview = (previewContentType, previewContent);
        return (previewContentType, previewContent);
    }
}

