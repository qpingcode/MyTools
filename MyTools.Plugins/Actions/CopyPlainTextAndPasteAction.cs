using MyTools.Common;
using MyTools.Common.Localization;
using MyTools.Plugins.Param;
using System.Windows;

namespace MyTools.Plugins;

public sealed class CopyPlainTextAndPasteAction : IAction
{
    public string Name => ActionText.Get(
        "Action.Clipboard.CopyPlainTextAndPaste.Name", "Copy as Plain Text and Paste");

    public string Description => ActionText.Get(
        "Action.Clipboard.CopyPlainTextAndPaste.Description",
        "Copy the text representation and paste it into the previously focused window");

    public async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not LazyClipboardParam clipboard || clipboard.GetPlainText() is not { } text)
        {
            return ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Clipboard.CopyPlainTextAndPaste.NoText",
                "This clipboard history item does not contain plain text."));
        }

        var dataObject = new DataObject();
        dataObject.SetText(text, TextDataFormat.UnicodeText);
        dataObject.SetData(DataObjectSerializer.MyToolsNotSaveHisotryFormat, "true");
        return await WellKnownActions.CopyAndPaste.ExecuteAsync(new ClipboardParam(dataObject));
    }
}
