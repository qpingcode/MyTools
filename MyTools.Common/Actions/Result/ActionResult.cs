namespace MyTools.Common;

public class ActionResult(bool success, string message, ActionTypeEnum actionType)
{
    public bool Success { get; } = success;
    public string Message { get; } = message;
    public ActionTypeEnum ActionType { get; } = actionType;
    
    public static ActionResult CreateSuccess(string message, ActionTypeEnum actionType = ActionTypeEnum.Close) => new(success: true, message: message, actionType);
    public static ActionResult CreateFailure(string error, ActionTypeEnum actionType = ActionTypeEnum.None) =>  new(success: false, message: error, actionType);
}