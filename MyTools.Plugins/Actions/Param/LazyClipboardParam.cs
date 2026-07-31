using System.Windows;
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

    object IClipboardSource.GetDataForClipboard()
    {
        // 延迟加载：只有在实际需要时才从数据库加载并反序列化
        if (_cachedDataObject == null)
        {
            var content = _dbHelper.GetContentById(_itemId);
            if (content == null)
            {
                throw new InvalidOperationException($"Clipboard history item with id {_itemId} not found");
            }
            var (dataObject, previewContentType, previewContent) = DataObjectSerializer.DeserializeToIDataObject(content);
            _cachedDataObject = dataObject;
            _cachedPreview = (previewContentType, previewContent);
        }
        return _cachedDataObject;
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

