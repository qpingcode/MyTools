namespace MyTools.Common;

public interface IPreviewContentProvider
{
    (PreviewContentType previewContentType, byte[] previewContent) GetPreviewContent();
}

