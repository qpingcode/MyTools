namespace MyTools.Desktop.Views;

internal static class PluginWindowCaptionDragLParam
{
    public static int PackScreenCoordinates(int x, int y)
    {
        var packedX = unchecked((ushort)(short)x);
        var packedY = unchecked((ushort)(short)y);
        return packedX | (packedY << 16);
    }
}
