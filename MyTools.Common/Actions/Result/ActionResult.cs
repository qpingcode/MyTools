using MyTools.Common.Localization;

namespace MyTools.Common;

public class ActionResult(bool success, string message, ActionTypeEnum actionType, LocalizedMessage? localizedMessage = null)
{
    public bool Success { get; } = success;
    public string Message { get; } = message;
    public LocalizedMessage? LocalizedMessage { get; } = localizedMessage;
    public ActionTypeEnum ActionType { get; } = actionType;
    
    public static ActionResult CreateSuccess(string message, ActionTypeEnum actionType = ActionTypeEnum.Close) => new(success: true, message: message, actionType);
    public static ActionResult CreateFailure(string error, ActionTypeEnum actionType = ActionTypeEnum.None) =>  new(success: false, message: error, actionType);
    public static ActionResult CreateSuccess(LocalizedMessage message, ActionTypeEnum actionType = ActionTypeEnum.Close) =>
        new(true, message.FormatFallback(), actionType, message);
    public static ActionResult CreateFailure(LocalizedMessage message, ActionTypeEnum actionType = ActionTypeEnum.None) =>
        new(false, message.FormatFallback(), actionType, message);
}